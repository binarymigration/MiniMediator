using BinaryMigration.MiniMediator;

namespace BinaryMigration.MiniMediatorTests;

public static class BuildMediatorTests
{
    public static IMediator BuildMediator(Dictionary<Type, List<object>> map)
    {
        IEnumerable<object> Factory(Type t) => map.TryGetValue(t, out var list) ? list : Array.Empty<object>();
        return new Mediator(Factory);
    }

    public sealed record MakeNumber(int Value) : IRequest<int>;
    public sealed record GetGreeting(string Name) : IQuery<string>;
    public sealed record UserSignedUp(string Email) : INotification;

    public sealed class MakeNumberHandler : IRequestHandler<MakeNumber, int>
    {
        public Task<int> Handle(MakeNumber request, CancellationToken ct) => Task.FromResult(request.Value + 1);
    }

    public sealed class GetGreetingHandler : IQueryHandler<GetGreeting, string>
    {
        public Task<string> Handle(GetGreeting query, CancellationToken ct) => Task.FromResult($"Hello, {query.Name}!");
    }

    public sealed class ThrowingHandler : IRequestHandler<MakeNumber, int>
    {
        public Task<int> Handle(MakeNumber request, CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    public sealed class CancellingHandler : IRequestHandler<MakeNumber, int>
    {
        public Task<int> Handle(MakeNumber request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(42);
        }
    }

    public sealed class LogRequestBehavior<TReq, TRes>(List<string> log, string name) : IRequestBehavior<TReq, TRes>
        where TReq : IRequest<TRes>
    {
        public async Task<TRes> Handle(TReq request, HandlerDelegate<TRes> next,CancellationToken ct)
        {
            log.Add($"{name}:pre");
            var res = await next().ConfigureAwait(false);
            log.Add($"{name}:post");
            return res;
        }
    }

    public sealed class LogQueryBehavior<Tq, TRes>(List<string> log, string name) : IQueryBehavior<Tq, TRes>
        where Tq : IQuery<TRes>
    {
        public async Task<TRes> Handle(Tq query,  HandlerDelegate<TRes> next, CancellationToken ct)
        {
            log.Add($"{name}:pre");
            var res = await next().ConfigureAwait(false);
            log.Add($"{name}:post");
            return res;
        }
    }

    public sealed class WelcomeEmail : INotificationHandler<UserSignedUp>
    {
        public static int Count;
        public Task Handle(UserSignedUp n, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }

    public sealed class TrackAnalytics : INotificationHandler<UserSignedUp>
    {
        public static int Count;
        public Task Handle(UserSignedUp n, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }
}
