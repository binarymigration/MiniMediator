using BinaryMigration.MiniMediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BinaryMigration.MiniMediatorTests;

public sealed class MediatorPublishOptionsTests
{
    public sealed record SomethingHappened : INotification;

    public sealed class OkHandler : INotificationHandler<SomethingHappened>
    {
        public static int Count;
        public Task Handle(SomethingHappened n, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }

    public sealed class BoomHandler1 : INotificationHandler<SomethingHappened>
    {
        public static int Count;
        public Task Handle(SomethingHappened n, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            Task.Delay(100, ct);
            throw new InvalidOperationException("boom #1");
        }
    }

    public sealed class BoomHandler2 : INotificationHandler<SomethingHappened>
    {
        public static int Count;
        public Task Handle(SomethingHappened n, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            Task.Delay(100, ct);
            throw new ApplicationException("boom #2");
        }
    }

    private static IMediator BuildMediator(PublishErrorMode mode, bool registerOkFirst, bool includeOk = true)
    {
        OkHandler.Count = 0; BoomHandler1.Count = 0; BoomHandler2.Count = 0;

        var services = new ServiceCollection();

        services.AddMediator(o => o.PublishErrors = mode);

        // Order matters for FailFast — let caller decide
        if (registerOkFirst)
        {
            if (includeOk)
            {
                services.AddNotificationHandler<SomethingHappened, OkHandler>();
            }

            services.AddNotificationHandler<SomethingHappened, BoomHandler1>();
            services.AddNotificationHandler<SomethingHappened, BoomHandler2>();
        }
        else
        {
            services.AddNotificationHandler<SomethingHappened, BoomHandler1>();
            services.AddNotificationHandler<SomethingHappened, BoomHandler2>();
            if (includeOk)
            {
                services.AddNotificationHandler<SomethingHappened, OkHandler>();
            }
        }

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Publish_Aggregate_Collects_All_Failures()
    {
        var mediator = BuildMediator(PublishErrorMode.Aggregate, registerOkFirst: true);
        var act = () => mediator.Publish(new SomethingHappened());

        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().HaveCount(2);
        OkHandler.Count.Should().Be(1);      // success handler still ran
        BoomHandler1.Count.Should().Be(1);   // all failing handler ran
        BoomHandler2.Count.Should().Be(1);
    }

    [Fact]
    public async Task Publish_FailFast_Stops_On_First_Failure_In_Registration_Order()
    {
        var mediator = BuildMediator(PublishErrorMode.FailFast, registerOkFirst: false, includeOk: false);
        // Registered order: Boom1, Boom2 (both throw). With FailFast, Boom1 throws, Boom2 should not run.
        var act = () => mediator.Publish(new SomethingHappened());

        await act.Should().ThrowAsync<InvalidOperationException>(); // first failure
        BoomHandler1.Count.Should().Be(1);
        BoomHandler2.Count.Should().Be(0); // did not reach second handler
    }

    [Fact]
    public async Task Publish_BestEffort_Invokes_All_And_Returns_Aggregate()
    {
        // NOTE: with the current implementation, BestEffort still rethrows AggregateException
        // (you can comment out those lines to fully swallow if you prefer).
        var mediator = BuildMediator(PublishErrorMode.BestEffort, registerOkFirst: true);
        var act = () => mediator.Publish(new SomethingHappened());

        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().HaveCount(2);
        OkHandler.Count.Should().Be(1);
        BoomHandler1.Count.Should().Be(1);
        BoomHandler2.Count.Should().Be(1);
    }
}
