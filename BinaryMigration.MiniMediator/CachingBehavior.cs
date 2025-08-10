using System.Collections.Concurrent;

namespace BinaryMigration.MiniMediator;

public sealed class CachingBehavior<Tq, TRes> : IQueryBehavior<Tq, TRes>
    where Tq : IQuery<TRes>
{
    private static readonly ConcurrentDictionary<string, TRes> _cache = new();

    public Task<TRes> Handle(Tq query, HandlerDelegate<TRes> next, CancellationToken ct)
    {
        var key = $"{typeof(Tq).FullName}:{System.Text.Json.JsonSerializer.Serialize(query)}";
        if (_cache.TryGetValue(key, out var val))
        {
            return Task.FromResult(val);
        }

        return Invoke();

        async Task<TRes> Invoke()
        {
            var res = await next();
            _cache[key] = res;
            return res;
        }
    }
}
