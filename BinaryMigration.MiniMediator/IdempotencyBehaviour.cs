using System.Collections.Concurrent;

namespace BinaryMigration.MiniMediator;

/// <summary>
/// Prevents re-executing the same command (request) by idempotency key.
/// - If a request implements IHasIdempotencyKey, that key is used.
/// - Otherwise, provide a key selector via ctor.
/// - Uses in-process coalescing (single execution) + IIdempotencyStore for result reuse.
/// </summary>
public sealed class IdempotencyBehavior<TReq, TRes>(
    IIdempotencyStore store,
    Func<TReq, string?>? keySelector = null,
    TimeSpan? ttl = null)
    : IRequestBehavior<TReq, TRes>
    where TReq : IRequest<TRes>
{
    private readonly IIdempotencyStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Func<TReq, string?>? _keySelector = keySelector;
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(10);

    // Prevents duplicate concurrent executions in-process
    private readonly ConcurrentDictionary<string, Lazy<Task<TRes>>> _inflight = new();

    public IdempotencyBehavior(IIdempotencyStore store, TimeSpan? ttl = null)
        : this(store, TrySelectDefaultKey, ttl) { }

    public async Task<TRes> Handle(TReq request, HandlerDelegate<TRes> next, CancellationToken ct)
    {
        var hasDefaultKey = request is IHasIdempotencyKey;
        if (!hasDefaultKey && _keySelector is null)
        {
            return await next().ConfigureAwait(false);
        }

        // If you still want to allow external override, combine it safely:
        var key = hasDefaultKey ? (request as IHasIdempotencyKey)?.IdempotencyKey : _keySelector!(request);
        if (string.IsNullOrWhiteSpace(key))
        {
            return await next().ConfigureAwait(false);
        }

        var cacheKey = BuildCacheKey(typeof(TReq), typeof(TRes), key);

        // 1) Return cached completed result if available
        if (_store.TryGet(cacheKey, out var cached) && cached is TRes cachedVal)
        {
            return cachedVal;
        }

        // 2) Coalesce concurrent executions
        var lazy = _inflight.GetOrAdd(cacheKey, _ => new Lazy<Task<TRes>>(() => next()));
        try
        {
            var result = await lazy.Value.ConfigureAwait(false);
            _store.Set(cacheKey, result!, _ttl);
            return result;
        }
        finally
        {
            _inflight.TryRemove(cacheKey, out _);
        }
    }

    private static string BuildCacheKey(Type req, Type res, string key) =>
        $"{req.FullName}|{res.FullName}|{key}";

    private static string TrySelectDefaultKey(TReq req) =>
        ((IHasIdempotencyKey)req).IdempotencyKey;
}
