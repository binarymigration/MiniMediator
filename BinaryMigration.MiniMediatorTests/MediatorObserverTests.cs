using System.Collections.Concurrent;
using BinaryMigration.MiniMediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BinaryMigration.MiniMediatorTests;

public sealed class MediatorObserverTests
{
    public sealed class TestObserver : IMediatorObserver
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public void OnDispatch(object message) => Events.Enqueue($"dispatch:{message.GetType().Name}");
        public void OnSuccess(object message, TimeSpan duration) => Events.Enqueue($"success:{message.GetType().Name}:{duration.TotalMilliseconds:F0}");
        public void OnError(object message, Exception exception, TimeSpan duration) => Events.Enqueue($"error:{message.GetType().Name}:{exception.GetType().Name}");
        public void OnPublish(object notification, int handlerCount) => Events.Enqueue($"publish:{notification.GetType().Name}:{handlerCount}");
    }

    public sealed record AddOne(int Value) : IRequest<int>;
    public sealed class AddOneHandler : IRequestHandler<AddOne, int>
    {
        public Task<int> Handle(AddOne request, CancellationToken ct) => Task.FromResult(request.Value + 1);
    }

    public sealed record Bad : IRequest<int>;
    public sealed class BadHandler : IRequestHandler<Bad, int>
    {
        public Task<int> Handle(Bad request, CancellationToken ct) => throw new InvalidOperationException("nope");
    }

    public sealed record Poke : INotification;
    public sealed class PokeHandler : INotificationHandler<Poke>
    {
        public Task Handle(Poke _, CancellationToken __) => Task.CompletedTask;
    }

    private static (IMediator mediator, TestObserver obs) Build()
    {
        var services = new ServiceCollection();

        // register mediator + options
        services.AddMediator(static opts => opts.PublishErrors = PublishErrorMode.Aggregate);

        // handlers
        services.AddNotificationHandler<Poke, PokeHandler>();

        // observer
        services.AddMediatorObserver<TestObserver>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        var obs = (TestObserver)sp.GetRequiredService<IMediatorObserver>();
        return (mediator, obs);
    }

    [Fact]
    public async Task Observer_Sees_Dispatch_And_Success_For_Requests()
    {
        var (mediator, obs) = Build();

        var result = await mediator.Send(new AddOne(10));
        result.Should().Be(11);

        obs.Events.Should().Contain(static e => e.StartsWith("dispatch:AddOne"));
        obs.Events.Should().Contain(static e => e.StartsWith("success:AddOne:"));
    }

    [Fact]
    public async Task Observer_Sees_Error_For_Failing_Requests()
    {
        var (mediator, obs) = Build();

        Func<Task> act = () => mediator.Send(new Bad());
        await act.Should().ThrowAsync<InvalidOperationException>();

        obs.Events.Should().Contain(static e => e.StartsWith("dispatch:Bad"));
        obs.Events.Should().Contain(static e => e.StartsWith("error:Bad:InvalidOperationException"));
    }

    [Fact]
    public async Task Observer_Sees_Publish_With_Handler_Count()
    {
        var (mediator, obs) = Build();

        await mediator.Publish(new Poke());
        obs.Events.Should().Contain(static e => e == "publish:Poke:1");
    }
}
