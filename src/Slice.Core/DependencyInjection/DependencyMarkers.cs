namespace Slice.Core.DependencyInjection;

/// <summary>Auto-register the implementing class (and its interfaces) as transient.</summary>
public interface ITransientDependency;

/// <summary>Auto-register the implementing class (and its interfaces) as scoped.</summary>
public interface IScopedDependency;

/// <summary>Auto-register the implementing class (and its interfaces) as singleton.</summary>
public interface ISingletonDependency;
