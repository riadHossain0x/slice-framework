using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.AspNetCore.AppConfig;

/// <summary>A navigation item for the frontend. Gate it with <see cref="RequiredPermission"/> and/or
/// <see cref="RequiredFeature"/> — the app-config provider drops items the current user/tenant can't see.</summary>
public sealed class MenuItem
{
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Url { get; init; }
    public string? Icon { get; init; }
    public int Order { get; init; }
    public string? RequiredPermission { get; init; }
    public string? RequiredFeature { get; init; }
    public List<MenuItem> Children { get; init; } = [];
}

public sealed class MenuBuilder
{
    private readonly List<MenuItem> _items = [];
    public IReadOnlyList<MenuItem> Items => _items;
    public MenuBuilder Add(MenuItem item) { _items.Add(item); return this; }
}

/// <summary>Implement (per module) to contribute navigation items; register with <c>AddSliceMenuContributors</c>.</summary>
public interface IMenuContributor
{
    Task ContributeAsync(MenuBuilder menu, CancellationToken ct = default);
}

public static class MenuRegistration
{
    /// <summary>Discovers and registers every <see cref="IMenuContributor"/> in <paramref name="assembly"/>.</summary>
    public static IServiceCollection AddSliceMenuContributors(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
            if (type is { IsClass: true, IsAbstract: false } && typeof(IMenuContributor).IsAssignableFrom(type))
                services.AddTransient(typeof(IMenuContributor), type);
        return services;
    }
}
