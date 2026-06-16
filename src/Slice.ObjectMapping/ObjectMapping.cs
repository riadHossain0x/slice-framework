using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.Core.DependencyInjection;
using Slice.Modularity;

namespace Slice.ObjectMapping;

/// <summary>A typed mapper for one source→destination pair (implement with Mapperly, by hand, etc.).</summary>
public interface IObjectMapper<in TSource, out TDestination>
{
    TDestination Map(TSource source);
}

/// <summary>Resolves the registered typed mapper for a given pair.</summary>
public interface IObjectMapper
{
    TDestination Map<TSource, TDestination>(TSource source);
}

public sealed class ObjectMapper(IServiceProvider serviceProvider) : IObjectMapper, ITransientDependency
{
    public TDestination Map<TSource, TDestination>(TSource source)
    {
        var mapper = serviceProvider.GetService<IObjectMapper<TSource, TDestination>>()
            ?? throw new InvalidOperationException(
                $"No IObjectMapper<{typeof(TSource).Name}, {typeof(TDestination).Name}> is registered.");
        return mapper.Map(source);
    }
}

/// <summary>Object-mapping module: registers the resolving <see cref="ObjectMapper"/>.</summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceObjectMappingModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceObjectMappingModule).Assembly);
}
