using BinaryMigration.MiniMediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BinaryMigration.MiniMediatorTests;

public class MediatorExtensionTests
{
    [Fact]
    public async Task OpenGeneric_Request_Behaviors_Apply_To_All_Requests_In_Registration_Order()
    {
        var (sp, mediator, log) = BuildMediatorExtensionsTests.BuildServices(
            static services =>
        {
            services.AddRequestBehavior(typeof(BuildMediatorExtensionsTests.RequestOuterBehavior<,>));
            services.AddRequestBehavior(typeof(BuildMediatorExtensionsTests.RequestInnerBehavior<,>));
        });

        var id = await mediator.Send(new BuildMediatorExtensionsTests.CreateInvoice("INV-1", 100m));
        id.Should().NotBe(Guid.Empty);

        log.Lines.Should().Equal("ReqOuter:pre", "ReqInner:pre", "ReqInner:post", "ReqOuter:post");
        await sp.DisposeAsync();
    }

    [Fact]
    public async Task OpenGeneric_Query_Behaviors_Apply_To_All_Queries_In_Registration_Order()
    {
        var (sp, mediator, log) = BuildMediatorExtensionsTests.BuildServices(
            static services =>
        {
            services.AddQueryBehavior(typeof(BuildMediatorExtensionsTests.QueryOuterBehavior<,>));
            services.AddQueryBehavior(typeof(BuildMediatorExtensionsTests.QueryInnerBehavior<,>));
        });

        var dto = await mediator.Send(new BuildMediatorExtensionsTests.GetInvoice("INV-007"));
        dto.Number.Should().Be("INV-007");
        dto.Amount.Should().Be(123.45m);

        log.Lines.Should().Equal("QryOuter:pre", "QryInner:pre", "QryInner:post", "QryOuter:post");
        await sp.DisposeAsync();
    }

    [Fact]
    public async Task Closed_Request_Behavior_Wraps_Only_Its_Specific_Request()
    {
        var (sp, mediator, log) = BuildMediatorExtensionsTests.BuildServices(
            static services =>
        {
            // Add an open generic to make the order obvious too
            services.AddRequestBehavior(typeof(BuildMediatorExtensionsTests.RequestOuterBehavior<,>));
            // Closed behavior applies only to CreateInvoice
            services.AddRequestBehavior<BuildMediatorExtensionsTests.CreateInvoice, Guid, BuildMediatorExtensionsTests.ClosedRequestBehavior>();
        });

        log.Lines.Clear();
        _ = await mediator.Send(new BuildMediatorExtensionsTests.CreateInvoice("X", 1m));
        log.Lines.Should().Equal("ReqOuter:pre", "ClosedReq:pre", "ClosedReq:post", "ReqOuter:post");

        log.Lines.Clear();
        _ = await mediator.Send(new BuildMediatorExtensionsTests.CreateInvoice("Y", 2m));
        log.Lines.Should().Equal("ReqOuter:pre", "ClosedReq:pre", "ClosedReq:post", "ReqOuter:post");

        await sp.DisposeAsync();
    }

    [Fact]
    public async Task Manual_Behaviors_Can_Wrap_Scanned_Ones_By_Positioning()
    {
        // Register outer behavior BEFORE AddMediator (wraps everything),
        // and inner behavior AFTER (becomes innermost).
        var services = new ServiceCollection();
        services.AddSingleton<BuildMediatorExtensionsTests.ITestLog, BuildMediatorExtensionsTests.TestLog>();

        // OUTERMOST
        services.AddRequestBehavior(typeof(BuildMediatorExtensionsTests.RequestOuterBehavior<,>));

        // scan handlers
        services.AddMediator();

        // INNERMOST
        services.AddRequestBehavior(typeof(BuildMediatorExtensionsTests.RequestInnerBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        var log = sp.GetRequiredService<BuildMediatorExtensionsTests.ITestLog>();

        _ = await mediator.Send(new BuildMediatorExtensionsTests.CreateInvoice("INV-2", 10m));

        log.Lines.Should().Equal("ReqOuter:pre", "ReqInner:pre", "ReqInner:post", "ReqOuter:post");
        await sp.DisposeAsync();
    }
}
