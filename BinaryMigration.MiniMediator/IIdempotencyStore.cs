namespace BinaryMigration.MiniMediator;

public interface IIdempotencyStore
{
    bool TryGet(string key, out object? value);
    void Set(string key, object value, TimeSpan ttl);
}
