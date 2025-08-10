namespace BinaryMigration.MiniMediator;

public enum PublishErrorMode
{
    /// <summary>Await all handlers; if any fail, throw AggregateException.</summary>
    Aggregate = 0,
    /// <summary>Invoke handlers sequentially; throw on first failure.</summary>
    FailFast = 1,
    /// <summary>Invoke all handlers; swallow/log errors (library collects; you can change rethrow behavior).</summary>
    BestEffort = 2,
}

public sealed class MediatorOptions
{
    public PublishErrorMode PublishErrors { get; set; } = PublishErrorMode.Aggregate;
}
