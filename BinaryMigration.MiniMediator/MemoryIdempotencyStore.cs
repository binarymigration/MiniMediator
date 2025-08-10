using System.Collections.Concurrent;

namespace BinaryMigration.MiniMediator;

public sealed class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (object Value, DateTimeOffset Expires)> _cache = new();

    public bool TryGet(string key, out object? value)
    {
        value = null;
        if (!_cache.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (entry.Expires < DateTimeOffset.UtcNow)
        {
            _cache.TryRemove(key, out _);
            return false;
        }
        value = entry.Value;
        return true;
    }

    public void Set(string key, object value, TimeSpan ttl)
    {
        var exp = DateTimeOffset.UtcNow.Add(ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : ttl);
        _cache[key] = (value, exp);
        // NOTE: simple; you can add a periodic cleanup if needed
    }
}
