using Microsoft.Extensions.DependencyInjection;
using Slice.Mediator;
using Slice.Mediator.Default;
using Slice.Mediator.MediatR;

namespace Slice.Mediator.Conformance.Tests;

// ── Test fixtures ────────────────────────────────────────────────────────────
public sealed record Ping(string Text) : IRequest<string>;

public sealed class Recorder { public List<string> Steps { get; } = []; }

public sealed class PingHandler(Recorder recorder) : IRequestHandler<Ping, string>
{
    public Task<string> HandleAsync(Ping request, CancellationToken ct)
    {
        recorder.Steps.Add("handler");
        return Task.FromResult($"pong:{request.Text}");
    }
}

// Lower Order = outermost. Registered AFTER InnerBehavior to prove ordering is by Order, not DI order.
public sealed class OuterBehavior<TReq, TRes>(Recorder recorder) : IPipelineBehavior<TReq, TRes>, IHasPipelineOrder
    where TReq : IRequest<TRes>
{
    public int Order => 100;
    public async Task<TRes> HandleAsync(TReq request, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        recorder.Steps.Add("outer-before");
        var r = await next();
        recorder.Steps.Add("outer-after");
        return r;
    }
}

public sealed class InnerBehavior<TReq, TRes>(Recorder recorder) : IPipelineBehavior<TReq, TRes>, IHasPipelineOrder
    where TReq : IRequest<TRes>
{
    public int Order => 400;
    public async Task<TRes> HandleAsync(TReq request, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        recorder.Steps.Add("inner-before");
        var r = await next();
        recorder.Steps.Add("inner-after");
        return r;
    }
}

// ── Tests ────────────────────────────────────────────────────────────────────
public class MediatorConformanceTests
{
    private static readonly string[] ExpectedOrder =
        ["outer-before", "inner-before", "handler", "inner-after", "outer-after"];

    private static (ISender Sender, Recorder Recorder) Build(Action<IServiceCollection> engine)
    {
        var services = new ServiceCollection();
        var recorder = new Recorder();
        services.AddSingleton(recorder);
        services.AddTransient<IRequestHandler<Ping, string>, PingHandler>();
        // intentionally register inner before outer
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>));
        engine(services);
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ISender>(), recorder);
    }

    [Fact]
    public async Task Default_engine_runs_handler_and_orders_behaviors()
    {
        var (sender, recorder) = Build(s => s.AddSliceMediator());

        var result = await sender.SendAsync(new Ping("x"));

        Assert.Equal("pong:x", result);
        Assert.Equal(ExpectedOrder, recorder.Steps);
    }

    [Fact]
    public async Task MediatR_engine_runs_handler_and_orders_behaviors()
    {
        var (sender, recorder) = Build(s => s.AddSliceMediatorMediatR());

        var result = await sender.SendAsync(new Ping("x"));

        Assert.Equal("pong:x", result);
        Assert.Equal(ExpectedOrder, recorder.Steps);
    }

    [Fact]
    public async Task Both_engines_produce_identical_behavior()
    {
        var (def, defRec) = Build(s => s.AddSliceMediator());
        var (med, medRec) = Build(s => s.AddSliceMediatorMediatR());

        var defResult = await def.SendAsync(new Ping("same"));
        var medResult = await med.SendAsync(new Ping("same"));

        Assert.Equal(defResult, medResult);
        Assert.Equal(defRec.Steps, medRec.Steps);
    }
}
