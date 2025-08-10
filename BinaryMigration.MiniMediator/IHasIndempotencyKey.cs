namespace BinaryMigration.MiniMediator;

public interface IHasIdempotencyKey
{
    string IdempotencyKey { get; }
}
