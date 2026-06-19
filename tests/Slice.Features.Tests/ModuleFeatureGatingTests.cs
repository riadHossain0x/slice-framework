using Slice.Application;
using Slice.Core.Results;
using Slice.Mediator;

namespace Slice.Features.Tests;

// Requests defined in THIS assembly — module-level gating keys on the request's assembly.
public sealed record SalesRequest : ICommand;                 // gated by the module requirement below
public sealed record PlainRequest : ICommand;                 // no attribute, no module requirement
[RequiresFeature("Other")]
public sealed record AttributedRequest : ICommand;            // gated by attribute only

public sealed class ModuleFeatureGatingTests
{
    private sealed class StubFeatureChecker(Func<string, bool> isEnabled) : IFeatureChecker
    {
        public Task<string?> GetOrNullAsync(string name) => Task.FromResult<string?>(isEnabled(name) ? "true" : "false");
        public Task<bool> IsEnabledAsync(string name) => Task.FromResult(isEnabled(name));
    }

    private static IModuleFeatureRegistry RegistryFor(string? feature) =>
        new ModuleFeatureRegistry(feature is null
            ? []
            : [new ModuleFeatureRequirement(typeof(SalesRequest).Assembly, feature)]);

    private static async Task<(bool nextCalled, Result result)> RunAsync<TRequest>(
        TRequest request, IFeatureChecker checker, IModuleFeatureRegistry registry)
        where TRequest : IRequest<Result>
    {
        var behavior = new RequiresFeatureBehavior<TRequest, Result>(checker, registry);
        var nextCalled = false;
        RequestHandlerDelegate<Result> next = () => { nextCalled = true; return Task.FromResult(Result.Success()); };
        var result = await behavior.HandleAsync(request, next, CancellationToken.None);
        return (nextCalled, result);
    }

    [Fact]
    public async Task Module_requirement_blocks_when_feature_disabled()
    {
        var (nextCalled, result) = await RunAsync(
            new SalesRequest(), new StubFeatureChecker(_ => false), RegistryFor("Sales"));

        Assert.False(nextCalled);          // handler never runs
        Assert.True(result.IsFailure);     // → Forbidden
    }

    [Fact]
    public async Task Module_requirement_passes_when_feature_enabled()
    {
        var (nextCalled, result) = await RunAsync(
            new SalesRequest(), new StubFeatureChecker(_ => true), RegistryFor("Sales"));

        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task No_requirement_passes_even_when_features_disabled()
    {
        var (nextCalled, result) = await RunAsync(
            new PlainRequest(), new StubFeatureChecker(_ => false), RegistryFor(feature: null));

        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Attribute_and_module_requirements_compose()
    {
        // AttributedRequest requires "Other" (attribute) AND "Sales" (module). Only "Other" enabled → still blocked.
        var (nextCalled, result) = await RunAsync(
            new AttributedRequest(),
            new StubFeatureChecker(name => name == "Other"),
            RegistryFor("Sales"));

        Assert.False(nextCalled);
        Assert.True(result.IsFailure);
    }
}
