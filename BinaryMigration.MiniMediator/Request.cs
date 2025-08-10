namespace BinaryMigration.MiniMediator;

// ReSharper disable once UnusedTypeParameter
public interface IRequest<out TResponse> { }

public interface IRequestBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, HandlerDelegate<TResponse> next, CancellationToken ct);
}

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}
