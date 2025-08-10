namespace BinaryMigration.MiniMediator;

public interface IMediatorObserver
{
    /// <summary>
    /// Called before a request or query is dispatched.
    /// </summary>
    void OnDispatch(object message);

    /// <summary>
    /// Called after a request or query is dispatched successfully.
    /// </summary>
    void OnSuccess(object message, TimeSpan duration);

    /// <summary>
    /// Called if a request or query fails.
    /// </summary>
    void OnError(object message, Exception exception, TimeSpan duration);

    /// <summary>
    /// Called before a notification is published.
    /// </summary>
    void OnPublish(object notification, int handlerCount);
}
