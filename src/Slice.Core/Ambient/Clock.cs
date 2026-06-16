using Slice.Core.DependencyInjection;

namespace Slice.Core.Ambient;

/// <summary>Default UTC clock.</summary>
public sealed class Clock : IClock, ISingletonDependency
{
    public DateTime Now => DateTime.UtcNow;
}
