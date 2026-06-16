using Slice.Core.DependencyInjection;

namespace Slice.Core.Ambient;

/// <summary>
/// Default <see cref="IGuidGenerator"/> producing time-ordered UUID v7 values
/// (index-friendly, unlike random v4 GUIDs).
/// </summary>
public sealed class SequentialGuidGenerator : IGuidGenerator, ISingletonDependency
{
    public Guid Create() => Guid.CreateVersion7();
}
