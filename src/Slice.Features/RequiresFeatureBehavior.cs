using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.Application.Results;
using Slice.Core.Results;
using Slice.Mediator;
using Slice.Modularity;

namespace Slice.Features;

/// <summary>Requires a feature to be enabled to execute a request.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresFeatureAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}

/// <summary>Short-circuits to <see cref="Error.Forbidden"/> when a required feature is disabled.</summary>
public sealed class RequiresFeatureBehavior<TRequest, TResponse>(IFeatureChecker featureChecker)
    : IPipelineBehavior<TRequest, TResponse>, IHasPipelineOrder
    where TRequest : IRequest<TResponse>
{
    private static readonly string[] Required =
        typeof(TRequest).GetCustomAttributes<RequiresFeatureAttribute>(inherit: true).Select(a => a.Feature).Distinct().ToArray();

    public int Order => PipelineOrder.FeatureCheck;

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        foreach (var feature in Required)
            if (!await featureChecker.IsEnabledAsync(feature))
                return ResultFactory.FailureOrThrow<TResponse>(
                    Error.Forbidden("Features:Disabled", $"Feature '{feature}' is not enabled."));

        return await next();
    }
}

[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceFeaturesModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSliceConventions(typeof(SliceFeaturesModule).Assembly);
        context.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequiresFeatureBehavior<,>));
    }
}
