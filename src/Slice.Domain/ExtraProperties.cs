using System.Text.Json;

namespace Slice.Domain;

/// <summary>A schema-less key/value bag persisted as a single JSON column (ABP-style extra properties).</summary>
public sealed class ExtraPropertyDictionary : Dictionary<string, object?>
{
    public ExtraPropertyDictionary() { }
    public ExtraPropertyDictionary(IDictionary<string, object?> dictionary) : base(dictionary) { }
}

/// <summary>An entity that carries an <see cref="ExtraPropertyDictionary"/> — ad-hoc data without a migration.</summary>
public interface IHasExtraProperties
{
    ExtraPropertyDictionary ExtraProperties { get; }
}

/// <summary>Typed, null-safe access to <see cref="IHasExtraProperties.ExtraProperties"/>.</summary>
public static class ExtraPropertyExtensions
{
    public static bool HasProperty(this IHasExtraProperties source, string name)
        => source.ExtraProperties.ContainsKey(name);

    public static object? GetProperty(this IHasExtraProperties source, string name)
        => source.ExtraProperties.GetValueOrDefault(name);

    /// <summary>
    /// Reads a property as <typeparamref name="T"/>. Values survive a JSON round-trip on load (coming
    /// back as <see cref="JsonElement"/>/string/number), so this converts as needed.
    /// </summary>
    public static T? GetProperty<T>(this IHasExtraProperties source, string name, T? defaultValue = default)
    {
        if (!source.ExtraProperties.TryGetValue(name, out var value) || value is null)
            return defaultValue;

        if (value is T typed)
            return typed;

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        // System.Text.Json hands back JsonElement after a load.
        if (value is JsonElement element)
            return element.Deserialize<T>() ?? defaultValue;

        try
        {
            if (targetType == typeof(Guid)) return (T)(object)Guid.Parse(value.ToString()!);
            if (targetType.IsEnum) return (T)Enum.Parse(targetType, value.ToString()!, ignoreCase: true);
            if (value is IConvertible) return (T)Convert.ChangeType(value, targetType);
            // Fall back to a JSON round-trip for complex types.
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>Sets a property (chainable).</summary>
    public static TSource SetProperty<TSource>(this TSource source, string name, object? value)
        where TSource : IHasExtraProperties
    {
        source.ExtraProperties[name] = value;
        return source;
    }

    public static TSource RemoveProperty<TSource>(this TSource source, string name)
        where TSource : IHasExtraProperties
    {
        source.ExtraProperties.Remove(name);
        return source;
    }
}
