namespace BinaryMigration.MiniMediator;

public interface INotification { }

public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken ct);
}
