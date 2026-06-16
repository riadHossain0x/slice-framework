namespace Slice.Mediator;

/// <summary>
/// Optional ordering hint for pipeline behaviors. Behaviors are chained by ascending
/// <see cref="Order"/> (lowest = outermost). Behaviors that don't implement this run innermost
/// (just before the handler), so cross-module registration order doesn't matter.
/// </summary>
public interface IHasPipelineOrder
{
    int Order { get; }
}

/// <summary>Canonical orders for the framework's standard behaviors (outermost → innermost).</summary>
public static class PipelineOrder
{
    public const int Logging = 100;
    public const int MultiTenancy = 200;
    public const int Authorization = 300;
    public const int FeatureCheck = 350;
    public const int Validation = 400;
    public const int UnitOfWork = 500;

    /// <summary>Behaviors without an explicit order run innermost.</summary>
    public const int Default = int.MaxValue;
}
