# MiniMediator

A lightweight, dependency-free CQRS-style mediator for C#, with:

- **Requests** (commands) — single handler
- **Queries** — single handler
- **Pipeline behaviors** per request/query type
- **Optional notifications** — multiple handlers
- **Observers** for monitoring dispatches and publishes
- **Idempotency behavior** for safe request retries
- Works without DI or with `Microsoft.Extensions.DependencyInjection`

---

## Features

- **Requests**: One handler per type (write side)
- **Queries**: One handler per type (read side)
- **Behaviors**: Middleware for logging, validation, caching, transactions, idempotency
- **Observers**: Hook into `OnDispatch`, `OnSuccess`, and `OnError` for all sends and publishes
- **Notifications**: Explicitly registered publish/subscribe events
- **Idempotency**: Built-in behavior that prevents duplicate processing when request implements `IHasIdempotencyKey`
- **No hidden magic**: Small, readable code

---

## Quick Start

### 1. Define a Request

```csharp
public sealed record CreateInvoice(string Number, decimal Amount) : IRequest<Guid>;

public sealed class CreateInvoiceHandler : IRequestHandler<CreateInvoice, Guid>
{
    public Task<Guid> Handle(CreateInvoice req, CancellationToken ct)
    {
        Console.WriteLine($"Creating invoice {req.Number}");
        return Task.FromResult(Guid.NewGuid());
    }
}
```
### 2. Define a Query

```csharp
public sealed record GetInvoice(string Number) : IQuery<InvoiceDto>;
public sealed record InvoiceDto(string Number, decimal Amount, string Status);

public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, InvoiceDto>
{
    public Task<InvoiceDto> Handle(GetInvoice q, CancellationToken ct)
        => Task.FromResult(new InvoiceDto(q.Number, 123.45m, "Created"));
}
```
### 3. Add Behaviors

```csharp
public sealed class LoggingBehavior<TReq, TRes> : IRequestBehavior<TReq, TRes>
    where TReq : IRequest<TRes>
{
    public async Task<TRes> Handle(TReq request, HandlerDelegate<TRes> next, CancellationToken ct)
    {
        Console.WriteLine($"--> {typeof(TReq).Name}");
        var res = await next();
        Console.WriteLine($"<-- {typeof(TReq).Name}");
        return res;
    }
}

public sealed class CachingBehavior<Tq, TRes> : IQueryBehavior<TQ, TRes>
    where TQ : IQuery<TRes>
{
    private static readonly Dictionary<string, TRes> Cache = new();

    public Task<TRes> Handle(TQ query, HandlerDelegate<TRes> next, CancellationToken ct)
    {
        var key = $"{typeof(TQ).FullName}:{System.Text.Json.JsonSerializer.Serialize(query)}";
        if (Cache.TryGetValue(key, out var val)) return Task.FromResult(val);
        return Invoke();

        async Task<TRes> Invoke()
        {
            var res = await next();
            Cache[key] = res;
            return res;
        }
    }
}
```
### 4. Add Idempotency Behavior
```csharp
public interface IHasIdempotencyKey
{
    string IdempotencyKey { get; }
}

// Behavior only applies if request implements IHasIdempotencyKey
public sealed class IdempotencyBehavior<TReq, TRes> : IRequestBehavior<TReq, TRes>
    where TReq : IRequest<TRes>
{
    private readonly IIdempotencyStore _store;
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, Lazy<Task<TRes>>> _inflight = new();

    public IdempotencyBehavior(IIdempotencyStore store) => _store = store;

    public async Task<TRes> Handle(TReq request, HandlerDelegate<TRes> next, CancellationToken ct)
    {
        if (request is not IHasIdempotencyKey hasKey || string.IsNullOrWhiteSpace(hasKey.IdempotencyKey))
            return await next();

        var key = $"{typeof(TReq).FullName}|{typeof(TRes).FullName}|{hasKey.IdempotencyKey}";

        if (_store.TryGet(key, out var cached) && cached is TRes hit)
            return hit;

        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<TRes>>(next));
        try
        {
            var result = await lazy.Value;
            _store.Set(key, result!, _ttl);
            return result;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }
}

```
### 5. DI Registration
```csharp
services.AddMediator(opts =>
{
    opts.PublishErrors = PublishErrorMode.Aggregate; // or FailFast, BestEffort
});

// Auto-scan requests & queries
// Notifications must be added manually:
services.AddNotificationHandler<UserSignedUp, SendWelcomeEmail>();

// Add behaviors
services.AddRequestBehavior(typeof(LoggingBehavior<,>));
services.AddQueryBehavior(typeof(CachingBehavior<,>));
services.AddRequestBehavior(typeof(IdempotencyBehavior<,>));

// Add observers
services.AddMediatorObserver<ConsoleMediatorObserver>();
```
### 6. Observers
Implement *IMediatorObserver* to hook into mediator events:
```csharp
public sealed class ConsoleMediatorObserver : IMediatorObserver
{
    public void OnDispatch(object message) =>
        Console.WriteLine($"Dispatching {message.GetType().Name}");

    public void OnSuccess(object message, TimeSpan duration) =>
        Console.WriteLine($"Success {message.GetType().Name} in {duration.TotalMs()} ms");

    public void OnError(object message, Exception ex, TimeSpan duration) =>
        Console.WriteLine($"Error {message.GetType().Name}: {ex.Message}");
}
```
### 7. Notifications
```csharp
public sealed record UserSignedUp(string Email) : INotification;

public sealed class SendWelcomeEmail : INotificationHandler<UserSignedUp>
{
    public Task Handle(UserSignedUp notification, CancellationToken ct)
    {
        Console.WriteLine($"Welcome email to {notification.Email}");
        return Task.CompletedTask;
    }
}
```
#### Register manually:
```csharp
services.AddNotificationHandler<UserSignedUp, SendWelcomeEmail>();
```
#### Publish anywhere:
```csharp
await mediator.Publish(new UserSignedUp("user@example.com"));
```
#### Exception Handling for Publish

MediatorOptions.PublishErrors controls error handling:
- FailFast: Stop on first failure
- BestEffort: Run all, collect errors, throw AggregateException
- Aggregate: Run all in parallel, collect errors, throw AggregateException

### 7. License
MIT License

