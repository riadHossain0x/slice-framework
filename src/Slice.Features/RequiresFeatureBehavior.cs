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

/// <summary>
/// Short-circuits to <see cref="Error.Forbidden"/> when a required feature is disabled. Requirements come
/// from <see cref="RequiresFeatureAttribute"/> on the request type <b>and</b> any module-level requirement
/// registered for the request's assembly via <c>RequireFeature&lt;TModule&gt;()</c> (see <see cref="ModuleFeatureGating"/>).
/// </summary>
public sealed class RequiresFeatureBehavior<TRequest, TResponse>(
    IFeatureChecker featureChecker, IModuleFeatureRegistry moduleFeatures)
    : IPipelineBehavior<TRequest, TResponse>, IHasPipelineOrder
    where TRequest : IRequest<TResponse>
{
    private static readonly string[] AttributeFeatures =
        typeof(TRequest).GetCustomAttributes<RequiresFeatureAttribute>(inherit: true).Select(a => a.Feature).Distinct().ToArray();

    public int Order => PipelineOrder.FeatureCheck;

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        foreach (var feature in AttributeFeatures.Concat(moduleFeatures.For(typeof(TRequest).Assembly)).Distinct())
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
