using BinaryMigration.MiniMediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BinaryMigration.MiniMediatorTests;

public sealed class IdempotencyBehaviorTests
{
    public sealed record CreateInvoice(string Number, string IdempotencyKey)
        : IRequest<Guid>, IHasIdempotencyKey;

    public sealed class CreateInvoiceHandler : IRequestHandler<CreateInvoice, Guid>
    {
        public static int Count;
        public Task<Guid> Handle(CreateInvoice req, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            // simulate some work
            return Task.FromResult(Guid.NewGuid());
        }
    }

    public sealed record ChargeCard(string Card, decimal Amount) : IRequest<string>;

    public sealed class ChargeCardHandler : IRequestHandler<ChargeCard, string>
    {
        public static int Count;
        public Task<string> Handle(ChargeCard req, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            return Task.FromResult($"OK:{req.Card}:{req.Amount}");
        }
    }

    private static ServiceProvider BuildDefaultServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        // mediator w/ default options
        services.AddMediator();

        // idempotency (memory store) + open-generic behavior
        services.AddSingleton<IIdempotencyStore, MemoryIdempotencyStore>();
        services.AddRequestBehavior(typeof(IdempotencyBehavior<,>));

        // handlers will be added by assembly scan
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Sequential_Calls_With_Same_Key_Use_Cache()
    {
        CreateInvoiceHandler.Count = 0;

        await using var sp = BuildDefaultServices();
        var mediator = sp.GetRequiredService<IMediator>();

        var key = "INV-42";
        var first = await mediator.Send(new CreateInvoice("INV-42",  key));
        var second = await mediator.Send(new CreateInvoice("INV-42", key));

        first.Should().NotBeEmpty();
        second.Should().Be(first);                // cached same result
        CreateInvoiceHandler.Count.Should().Be(1); // handler ran once
    }

    [Fact]
    public async Task Concurrent_Calls_With_Same_Key_Coalesce_To_Single_Execution()
    {
        CreateInvoiceHandler.Count = 0;

        await using var sp = BuildDefaultServices();
        var mediator = sp.GetRequiredService<IMediator>();

        var key = "INV-999";
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => mediator.Send(new CreateInvoice("INV-999", key)));
        var results = await Task.WhenAll(tasks);

        results.Distinct().Should().HaveCount(1);   // all got identical, cached Guid
        CreateInvoiceHandler.Count.Should().Be(1);  // only one execution
    }

    [Fact]
    public async Task Without_Key_Default_Behavior_Passes_Through_No_Cache()
    {
        ChargeCardHandler.Count = 0;

        await using var sp = BuildDefaultServices();
        var mediator = sp.GetRequiredService<IMediator>();

        // no IHasIdempotencyKey, open-generic behavior will see no key -> passthrough
        _ = await mediator.Send(new ChargeCard("411111", 10m));
        _ = await mediator.Send(new ChargeCard("411111", 10m));

        ChargeCardHandler.Count.Should().Be(2);
    }

    [Fact]
    public async Task Custom_Selector_Makes_NonKeyed_Request_Idempotent()
    {
        ChargeCardHandler.Count = 0;

        // build with custom CLOSED behavior for ChargeCard using card+amount as key
        ServiceProvider BuildWithCustomSelector()
        {
            return BuildDefaultServices(
                static services =>
            {
                // remove open-generic for this message OR just add closed one after (it will be inner, still fine)
                services.AddSingleton<IRequestBehavior<ChargeCard, string>>(
                    static sp =>
                {
                    var store = sp.GetRequiredService<IIdempotencyStore>();
                    // key selector: card+amount
                    return new IdempotencyBehavior<ChargeCard, string>(
                        store,
                        static req => $"{req.Card}:{req.Amount}",
                        ttl: TimeSpan.FromMinutes(5));
                });
            });
        }

        await using var sp = BuildWithCustomSelector();
        var mediator = sp.GetRequiredService<IMediator>();

        var r1 = await mediator.Send(new ChargeCard("411111", 10m));
        var r2 = await mediator.Send(new ChargeCard("411111", 10m));
        var r3 = await mediator.Send(new ChargeCard("411111", 12m));

        r1.Should().Be(r2);                        // same key -> cached
        r3.Should().NotBe(r1);                     // different amount -> different key
        ChargeCardHandler.Count.Should().Be(2);    // executed for first (10) and for (12)
    }
}
