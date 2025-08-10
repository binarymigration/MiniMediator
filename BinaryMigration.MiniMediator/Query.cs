namespace BinaryMigration.MiniMediator;

// ReSharper disable once UnusedTypeParameter
public interface IQuery<out TResponse> { }

public interface IQueryBehavior<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> Handle(TQuery query, HandlerDelegate<TResponse> next, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> Handle(TQuery query, CancellationToken ct);
}
