using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace BinaryMigration.MiniMediator;

public sealed class Mediator(
    ServiceFactory factory,
    MediatorOptions? options = null,
    IEnumerable<IMediatorObserver>? observers = null) : IMediator
{
    private readonly ServiceFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly MediatorOptions _options = options ?? new MediatorOptions();

    private static readonly ConcurrentDictionary<Type, MethodInfo> _handleMethodCache = new();
    private readonly IEnumerable<IMediatorObserver> _observers = observers ?? [];

    private static readonly ConcurrentDictionary<(Type Req, Type Res, bool IsQuery), Func<object, object, CancellationToken, Task<object>>> _compiledHandlerInvoker = new();

    private static async Task<object?> BoxResult<T>(Task<T> task) => await task.ConfigureAwait(false);

    private static Func<object, object, CancellationToken, Task<object>> GetOrCreateHandlerInvoker(Type requestType, Type responseType, bool isQuery)
    {
        return _compiledHandlerInvoker.GetOrAdd((requestType, responseType, isQuery), tuple =>
        {
            var (reqT, resT, _) = tuple;

            // Build: (handler, message, ct) => BoxResult( ((IRequestHandler<TReq,TRes>)handler).Handle((TReq)message, ct) )
            var handlerParam = Expression.Parameter(typeof(object), "handler");
            var msgParam = Expression.Parameter(typeof(object), "message");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

            var handlerInterfaceDef = isQuery ? typeof(IQueryHandler<,>) : typeof(IRequestHandler<,>);
            var handlerInterface = handlerInterfaceDef.MakeGenericType(reqT, resT);

            var castHandler = Expression.Convert(handlerParam, handlerInterface);
            var castMsg = Expression.Convert(msgParam, reqT);

            var handleMi = handlerInterface.GetMethod("Handle", BindingFlags.Instance | BindingFlags.Public)!; // Task<TRes> Handle(TReq, CT)

            var callHandle = Expression.Call(castHandler, handleMi, castMsg, ctParam); // Task<TRes>

            var boxMi = typeof(Mediator).GetMethod(nameof(BoxResult), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(resT);
            var callBox = Expression.Call(boxMi, callHandle); // Task<object>

            var lambda = Expression.Lambda<Func<object, object, CancellationToken, Task<object>>>(callBox, handlerParam, msgParam, ctParam);
            return lambda.Compile();
        });
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        => DispatchAsync<TResponse>(request, typeof(IRequestHandler<,>), typeof(IRequestBehavior<,>), ct);

    public Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
        => DispatchAsync<TResponse>(query, typeof(IQueryHandler<,>), typeof(IQueryBehavior<,>), ct);

    private async Task<TResponse> DispatchAsync<TResponse>(
    object message,
    Type handlerInterfaceDef,
    Type behaviorInterfaceDef,
    CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(message);

    var msgType = message.GetType();

    var handlerIfc = handlerInterfaceDef.MakeGenericType(msgType, typeof(TResponse));
    var handlerCandidates = _factory(handlerIfc).ToList();
    if (handlerCandidates.Count == 0)
    {
        throw new InvalidOperationException($"Handler for {msgType.Name} not found.");
    }

    if (handlerCandidates.Count > 1)
    {
        throw new InvalidOperationException($"Multiple handlers for {msgType.Name} found: {handlerCandidates.Count}.");
    }

    var handler = handlerCandidates[0];

    var invoker = GetOrCreateHandlerInvoker(msgType, typeof(TResponse), isQuery: handlerInterfaceDef == typeof(IQueryHandler<,>));
    Task<TResponse> InvokeHandler() =>
        InvokeUnwrap(async () => (TResponse)await invoker(handler, message, ct).ConfigureAwait(false));

    var behaviorIfc = behaviorInterfaceDef.MakeGenericType(msgType, typeof(TResponse));
    var behaviorMethod = GetHandleMethod(behaviorIfc);
    var behaviors = _factory(behaviorIfc).ToArray();

    HandlerDelegate<TResponse> next = InvokeHandler;
    for (var i = behaviors.Length - 1; i >= 0; i--)
    {
        var b = behaviors[i];
        var prev = next;
        next = () => InvokeUnwrap(() => (Task<TResponse>)behaviorMethod.Invoke(b, [message, prev, ct])!);
    }

    var started = DateTime.UtcNow;
    foreach (var obs in _observers)
    {
        obs.OnDispatch(message);
    }

    try
    {
        var result = await next().ConfigureAwait(false);
        var duration = DateTime.UtcNow - started;
        foreach (var obs in _observers)
        {
            obs.OnSuccess(message, duration);
        }

        return result;
    }
    catch (Exception ex)
    {
        var duration = DateTime.UtcNow - started;
        foreach (var obs in _observers)
        {
            obs.OnError(message, ex, duration);
        }

        throw;
    }
}
    public async Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        var ifc = typeof(INotificationHandler<>).MakeGenericType(notification.GetType());
        var handle = GetHandleMethod(ifc);
        var handlers = _factory(ifc).ToArray();

        foreach (var obs in _observers)
        {
            obs.OnPublish(notification, handlers.Length);
        }

        switch (_options.PublishErrors)
        {
            case PublishErrorMode.FailFast:
            {
                foreach (var h in _factory(ifc))
                {
                    var task = InvokeUnwrap(() => (Task)handle.Invoke(h, [notification, ct])!);
                    await task.ConfigureAwait(false);
                }
                break;
            }
            case PublishErrorMode.BestEffort:
            {
                List<Exception>? errors = null;
                foreach (var h in _factory(ifc))
                {
                    try
                    {
                        var task = InvokeUnwrap(() => (Task)handle.Invoke(h, [notification, ct])!);
                        await task.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        (errors ??= []).Add(ex);
                    }
                }

                if (errors is not null && errors.Count > 0)
                {
                    throw new AggregateException(errors);
                }

                break;
            }
            default:
            {
                var tasks = handlers.Select(h =>
                                                InvokeUnwrap(() => (Task) handle.Invoke(h, [notification, ct])!)
                ).ToArray();

                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch
                {
                    // Collect ALL errors (including sync ones turned into faulted tasks)
                    var errors = tasks
                        .Where(static t => t is { IsFaulted: true, Exception: not null })
                        .SelectMany(static t => t.Exception!.Flatten().InnerExceptions)
                        .ToArray();

                    throw new AggregateException(errors);
                }
                break;
            }
        }
    }

    private static T InvokeUnwrap<T>(Func<T> invoker)
    {
        try { return invoker(); }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    private static Task InvokeUnwrap(Func<Task> action)
    {
        try
        {
            return action(); // may already be a running Task
        }
        catch (TargetInvocationException tie)
        {
            return Task.FromException(tie.InnerException ?? tie);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private static MethodInfo GetHandleMethod(Type closedInterface)
        => _handleMethodCache.GetOrAdd(closedInterface, static t =>
        {
            var mi = t.GetMethod("Handle", BindingFlags.Instance | BindingFlags.Public);
            if (mi is null)
            {
                throw new MissingMethodException($"Handle method not found on {t.FullName}.");
            }

            return mi;
        });
}
