namespace BinaryMigration.MiniMediator;

public delegate IEnumerable<object> ServiceFactory(Type type);

public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
    Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification;
}
