namespace Slice.Modularity;

/// <summary>Declares the modules a <see cref="SliceModule"/> depends on (configured before it).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DependsOnAttribute(params Type[] dependedModuleTypes) : Attribute
{
    public IReadOnlyList<Type> DependedModuleTypes { get; } = dependedModuleTypes;
}
