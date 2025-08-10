using BinaryMigration.MiniMediator;
using FluentAssertions;

namespace BinaryMigration.MiniMediatorTests;

public sealed class MediatorTests
{
    [Fact]
    public async Task Send_Request_Returns_Response()
    {
        var map = new Dictionary<Type, List<object>>
        {
            [typeof(IRequestHandler<BuildMediatorTests.MakeNumber, int>)] = [new BuildMediatorTests.MakeNumberHandler()]
        };

        var mediator = BuildMediatorTests.BuildMediator(map);
        var res = await mediator.Send(new BuildMediatorTests.MakeNumber(10));
        res.Should().Be(11);
    }

    [Fact]
    public async Task Send_Query_Returns_Response()
    {
        var map = new Dictionary<Type, List<object>>
        {
            [typeof(IQueryHandler<BuildMediatorTests.GetGreeting, string>)] = [new BuildMediatorTests.GetGreetingHandler()]
        };

        var mediator = BuildMediatorTests.BuildMediator(map);
        var res = await mediator.Send(new BuildMediatorTests.GetGreeting("Ada"));
        res.Should().Be("Hello, Ada!");
    }

    private static readonly string[] _expectedRequestsOrder = ["A:pre", "B:pre", "B:post", "A:post"];
    private static readonly string[] _expectedQueriesOrder = ["Outer:pre", "Inner:pre", "Inner:post", "Outer:post"];

    [Fact]
    public async Task Request_Behaviors_Run_In_Declared_Order()
    {
        var log = new List<string>();

        var map = new Dictionary<Type, List<object>>
        {
            [typeof(IRequestHandler<BuildMediatorTests.MakeNumber, int>)] =
                [new BuildMediatorTests.MakeNumberHandler()],
            [typeof(IRequestBehavior<BuildMediatorTests.MakeNumber, int>)] =
            [
                new BuildMediatorTests.LogRequestBehavior<BuildMediatorTests.MakeNumber, int>(log, "A"),
                new BuildMediatorTests.LogRequestBehavior<BuildMediatorTests.MakeNumber, int>(log, "B")
            ]
        };

        var mediator = BuildMediatorTests.BuildMediator(map);
        var res = await mediator.Send(new BuildMediatorTests.MakeNumber(1));
        res.Should().Be(2);

        log.Should().Equal(_expectedRequestsOrder);
    }

    [Fact]
    public async Task Query_Behaviors_Run_In_Declared_Order()
    {
        var log = new List<string>();

        var map = new Dictionary<Type, List<object>>
        {
            [typeof(IQueryHandler<BuildMediatorTests.GetGreeting, string>)] =
                [new BuildMediatorTests.GetGreetingHandler()],
            [typeof(IQueryBehavior<BuildMediatorTests.GetGreeting, string>)] =
            [
                new BuildMediatorTests.LogQueryBehavior<BuildMediatorTests.GetGreeting, string>(log, "Outer"),
                new BuildMediatorTests.LogQueryBehavior<BuildMediatorTests.GetGreeting, string>(log, "Inner")
            ]
        };

        var mediator = BuildMediatorTests.BuildMediator(map);
        var res = await mediator.Send(new BuildMediatorTests.GetGreeting("Linus"));
        res.Should().Be("Hello, Linus!");
        log.Should().Equal(_expectedQueriesOrder);
    }

    [Fact]
    public async Task Publish_Invokes_All_Notification_Handlers()
    {
        BuildMediatorTests.WelcomeEmail.Count = 0;
        BuildMediatorTests.TrackAnalytics.Count = 0;

        var map = new Dictionary<Type, List<object>>
        {
            [typeof(INotificationHandler<BuildMediatorTests.UserSignedUp>)] =
                [new BuildMediatorTests.WelcomeEmail(), new BuildMediatorTests.TrackAnalytics()]
        };

        var mediator = BuildMediatorTests.BuildMediator(map);
        await mediator.Publish(new BuildMediatorTests.UserSignedUp("x@y.z"));

        BuildMediatorTests.WelcomeEmail.Count.Should().Be(1);
        BuildMediatorTests.TrackAnalytics.Count.Should().Be(1);
    }

    [Fact]
    public async Task Send_Throws_When_No_Handler()
    {
        var mediator = BuildMediatorTests.BuildMediator(new Dictionary<Type, List<object>>());
        Func<Task> act = () => mediator.Send(new BuildMediatorTests.MakeNumber(5));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task Send_Throws_When_Multiple_Handlers()
    {
        var map = new Dictionary<Type, List<object>>
        {
            [typeof(IRequestHandler<BuildMediatorTests.MakeNumber, int>)] =
                [new BuildMediatorTests.MakeNumberHandler(), new BuildMediatorTests.ThrowingHandler()]
        };

        var mediator = BuildMediatorTests.BuildMediator(map);
        Func<Task> act = () => mediator.Send(new BuildMediatorTests.MakeNumber(5));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Multiple handlers*");
    }

    [Fact]
    public async Task Cancellation_Is_Propagated_To_Handler()
    {
        var map = new Dictionary<Type, List<object>>
        {
            [typeof(IRequestHandler<BuildMediatorTests.MakeNumber, int>)] = [new BuildMediatorTests.CancellingHandler()]
        };

        var mediator = BuildMediatorTests.BuildMediator(map);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // ReSharper disable once AccessToDisposedClosure
        var act = () => mediator.Send(new BuildMediatorTests.MakeNumber(0), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
