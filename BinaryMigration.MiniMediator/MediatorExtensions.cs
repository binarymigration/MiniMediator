using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;

namespace BinaryMigration.MiniMediator;

public static class MediatorExtensions
{
    /// <summary>
    /// Registers Mediator + scans only solution assemblies (DependencyContext project types).
    /// Returns IServiceCollection for fluent chaining.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services, Action<MediatorOptions>? configure = null)
    {
        // options
        var opts = new MediatorOptions();
        configure?.Invoke(opts);

        // mediator with factory
        services.AddSingleton<IMediator>(sp =>
        {
            IEnumerable<object> Factory(Type t) => sp.GetServices(t).OfType<object>();
            var observers = sp.GetServices<IMediatorObserver>();
            return new Mediator(Factory, opts, observers);
        });

        var assemblies = GetSolutionAssemblies().ToArray();
        RegisterTypes(services, assemblies);

        return services;
    }

    /// <summary>
    /// Registers an open-generic request behavior for all requests.
    /// The order you call this determines pipeline order (first = outermost).
    /// </summary>
    public static IServiceCollection AddRequestBehavior(this IServiceCollection services, Type openGenericBehavior)
    {
        ArgumentNullException.ThrowIfNull(openGenericBehavior);
        if (!openGenericBehavior.IsGenericTypeDefinition)
        {
            throw new ArgumentException("Behavior must be an open generic type, e.g. typeof(LoggingBehavior<,>).", nameof(openGenericBehavior));
        }

        services.AddTransient(typeof(IRequestBehavior<,>), openGenericBehavior);
        return services;
    }

    /// <summary>
    /// Registers a closed request behavior for a specific request/response pair.
    /// </summary>
    public static IServiceCollection AddRequestBehavior<TReq, TRes, TBehavior>(this IServiceCollection services)
        where TReq : IRequest<TRes>
        where TBehavior : class, IRequestBehavior<TReq, TRes>
    {
        services.AddTransient<IRequestBehavior<TReq, TRes>, TBehavior>();
        return services;
    }

    /// <summary>
    /// Registers an open-generic query behavior for all queries.
    /// </summary>
    public static IServiceCollection AddQueryBehavior(this IServiceCollection services, Type openGenericBehavior)
    {
        ArgumentNullException.ThrowIfNull(openGenericBehavior);
        if (!openGenericBehavior.IsGenericTypeDefinition)
        {
            throw new ArgumentException("Behavior must be an open generic type, e.g. typeof(MetricsBehavior<,>).", nameof(openGenericBehavior));
        }

        services.AddTransient(typeof(IQueryBehavior<,>), openGenericBehavior);
        return services;
    }

    /// <summary>
    /// Registers a closed query behavior for a specific query/response pair.
    /// </summary>
    public static IServiceCollection AddQueryBehavior<TQuery, TRes, TBehavior>(this IServiceCollection services)
        where TQuery : IQuery<TRes>
        where TBehavior : class, IQueryBehavior<TQuery, TRes>
    {
        services.AddTransient<IQueryBehavior<TQuery, TRes>, TBehavior>();
        return services;
    }

    /// <summary>
    // Registers a single closed notification handler explicitly
    /// <summary>
    public static IServiceCollection AddNotificationHandler<TNotification, THandler>(this IServiceCollection services)
        where TNotification : INotification
        where THandler : class, INotificationHandler<TNotification>
    {
        services.AddTransient<INotificationHandler<TNotification>, THandler>();
        return services;
    }

    // Register an open-generic notification handler (e.g., typeof(MyHandler<>))
    public static IServiceCollection AddNotificationHandler(this IServiceCollection services, Type openGenericHandler)
    {
        ArgumentNullException.ThrowIfNull(openGenericHandler);

        if (!openGenericHandler.IsGenericTypeDefinition)
        {
            throw new ArgumentException("Handler must be an open generic type, e.g. typeof(MyHandler<>).", nameof(openGenericHandler));
        }

        // Ensure it implements INotificationHandler<> as an open generic
        var implements =
            openGenericHandler.GetInterfaces()
                .Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));

        if (!implements)
        {
            throw new ArgumentException("Type must implement INotificationHandler<>.", nameof(openGenericHandler));
        }

        services.AddTransient(typeof(INotificationHandler<>), openGenericHandler);
        return services;
    }

    public static IServiceCollection AddMediatorObserver<T>(this IServiceCollection services)
        where T : class, IMediatorObserver
    {
        services.AddSingleton<IMediatorObserver, T>();
        return services;
    }

    private static IEnumerable<Assembly> GetSolutionAssemblies()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // DependencyContext: only "project" libs (solution projects)
        var dc = DependencyContext.Default;
        if (dc is not null)
        {
            foreach (var lib in dc.RuntimeLibraries)
            {
                if (!string.Equals(lib.Type, "project", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Assembly? asm = null;
                try { asm = Assembly.Load(new AssemblyName(lib.Name)); }
                catch { /* ignore */ }

                if (asm is not null && yielded.Add(asm.FullName!))
                {
                    yield return asm;
                }
            }
        }
    }

    private static void RegisterTypes(IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(static t => t is not null).ToArray()!; }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                // Register closed implementations
                foreach (var i in type.GetInterfaces().Where(static ii => ii.IsGenericType))
                {
                    var def = i.GetGenericTypeDefinition();
                    if (def == typeof(IRequestHandler<,>) ||
                        def == typeof(IQueryHandler<,>))
                    {
                        if (!type.IsGenericTypeDefinition)
                        {
                            services.AddTransient(i, type);
                        }
                    }
                }

                // Register open-generic implementations
                if (type.IsGenericTypeDefinition)
                {
                    foreach (var i in type.GetInterfaces())
                    {
                        if (!i.IsGenericType)
                        {
                            continue;
                        }

                        var def = i.GetGenericTypeDefinition();
                        if (def == typeof(IRequestHandler<,>) ||
                            def == typeof(IQueryHandler<,>))
                        {
                            services.AddTransient(def, type);
                        }
                    }
                }
            }
        }
    }
}
