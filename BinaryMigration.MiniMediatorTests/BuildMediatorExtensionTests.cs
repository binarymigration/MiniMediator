using BinaryMigration.MiniMediator;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BinaryMigration.MiniMediatorTests;

public sealed class BuildMediatorExtensionsTests
{
    public interface ITestLog { IList<string> Lines { get; } }
    public sealed class TestLog : ITestLog { public IList<string> Lines { get; } = new List<string>(); }

    public sealed class RequestOuterBehavior<TReq, TRes>(ITestLog log) : IRequestBehavior<TReq, TRes> where TReq : IRequest<TRes>
    {
        public async Task<TRes> Handle(TReq request, HandlerDelegate<TRes> next, CancellationToken ct)
        {
            log.Lines.Add("ReqOuter:pre");
            var res = await next().ConfigureAwait(false);
            log.Lines.Add("ReqOuter:post");
            return res;
        }
    }

    public sealed class RequestInnerBehavior<TReq, TRes>(ITestLog log) : IRequestBehavior<TReq, TRes> where TReq : IRequest<TRes>
    {
        public async Task<TRes> Handle(TReq request, HandlerDelegate<TRes> next, CancellationToken ct)
        {
            log.Lines.Add("ReqInner:pre");
            var res = await next().ConfigureAwait(false);
            log.Lines.Add("ReqInner:post");
            return res;
        }
    }

    public sealed class QueryOuterBehavior<Tq, TRes> : IQueryBehavior<Tq, TRes>
        where Tq : IQuery<TRes>
    {
        private readonly ITestLog _log;
        public QueryOuterBehavior(ITestLog log) => _log = log;
        public async Task<TRes> Handle(Tq query, HandlerDelegate<TRes> next, CancellationToken ct)
        {
            _log.Lines.Add("QryOuter:pre");
            var res = await next().ConfigureAwait(false);
            _log.Lines.Add("QryOuter:post");
            return res;
        }
    }

    public sealed class QueryInnerBehavior<Tq, TRes>(ITestLog log) : IQueryBehavior<Tq, TRes> where Tq : IQuery<TRes>
    {
        public async Task<TRes> Handle(Tq query, HandlerDelegate<TRes> next, CancellationToken ct)
        {
            log.Lines.Add("QryInner:pre");
            var res = await next().ConfigureAwait(false);
            log.Lines.Add("QryInner:post");
            return res;
        }
    }

    public sealed class ClosedRequestBehavior(ITestLog log) : IRequestBehavior<CreateInvoice, Guid>
    {
        public async Task<Guid> Handle(CreateInvoice request, HandlerDelegate<Guid> next,  CancellationToken ct)
        {
            log.Lines.Add("ClosedReq:pre");
            var res = await next().ConfigureAwait(false);
            log.Lines.Add("ClosedReq:post");
            return res;
        }
    }

    public sealed record CreateInvoice(string Number, decimal Amount) : IRequest<Guid>;
    public sealed class CreateInvoiceHandler : IRequestHandler<CreateInvoice, Guid>
    {
        public Task<Guid> Handle(CreateInvoice req, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
    }

    public sealed record GetInvoice(string Number) : IQuery<InvoiceDto>;
    public sealed record InvoiceDto(string Number, decimal Amount);
    public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, InvoiceDto>
    {
        public Task<InvoiceDto> Handle(GetInvoice q, CancellationToken ct) =>
            Task.FromResult(new InvoiceDto(q.Number, 123.45m));
    }

    public static (ServiceProvider sp, IMediator mediator, ITestLog log) BuildServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        // logger for behaviors
        services.AddSingleton<ITestLog, TestLog>();

        // your mediator with scanning
        services.AddMediator().Should().NotBeNull();

        // allow caller to register manual behaviors around the scan
        configure(services);

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        var log = sp.GetRequiredService<ITestLog>();
        return (sp, mediator, log);
    }
}
