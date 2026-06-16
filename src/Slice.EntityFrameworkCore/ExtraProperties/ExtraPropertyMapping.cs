using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Slice.Domain;

namespace Slice.EntityFrameworkCore.ExtraProperties;

/// <summary>
/// Maps every <see cref="IHasExtraProperties"/> entity's <c>ExtraProperties</c> bag to a single JSON
/// column and provides a server-side equality filter (<see cref="SliceExtraProperties.JsonValue"/> /
/// <c>WhereExtraProperty</c>).
/// </summary>
public static class ExtraPropertyMapping
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly ValueConverter<ExtraPropertyDictionary, string> Converter = new(
        bag => JsonSerializer.Serialize(bag, Json),
        text => Deserialize(text));

    private static readonly ValueComparer<ExtraPropertyDictionary> Comparer = new(
        (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
        bag => JsonSerializer.Serialize(bag, Json).GetHashCode(),
        bag => new ExtraPropertyDictionary(bag));   // snapshot clone so edits are detected

    private static ExtraPropertyDictionary Deserialize(string text)
        => string.IsNullOrWhiteSpace(text)
            ? new ExtraPropertyDictionary()
            : new ExtraPropertyDictionary(JsonSerializer.Deserialize<Dictionary<string, object?>>(text, Json) ?? new());

    /// <summary>
    /// Maps the <c>ExtraProperties</c> bag of every <see cref="IHasExtraProperties"/> entity to a JSON
    /// column (<c>jsonb</c> on Npgsql, text elsewhere) and registers the JSON-extract filter function
    /// translated for the active provider. Call once from a context's <c>OnModelCreating</c>.
    /// </summary>
    public static ModelBuilder ConfigureExtraProperties(this ModelBuilder modelBuilder, string? providerName)
    {
        var isNpgsql = providerName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || !typeof(IHasExtraProperties).IsAssignableFrom(entityType.ClrType))
                continue;

            var property = modelBuilder.Entity(entityType.ClrType)
                .Property(typeof(ExtraPropertyDictionary), nameof(IHasExtraProperties.ExtraProperties))
                .HasColumnName("ExtraProperties")
                .HasConversion(Converter, Comparer);

            if (isNpgsql)
                property.HasColumnType("jsonb");
        }

        var fn = typeof(SliceExtraProperties).GetMethod(nameof(SliceExtraProperties.JsonValue))!;
        var fnBuilder = modelBuilder.HasDbFunction(fn);
        fnBuilder.HasParameter("properties").HasStoreType(isNpgsql ? "jsonb" : "TEXT");
        fnBuilder.HasTranslation(args =>
        {
            var column = args[0];
            var keyArg = args[1];
            var key = (string)((SqlConstantExpression)keyArg).Value!;

            // SQLite extracts via a JSON path ('$.key'); Postgres via the key segment.
            var (function, argument) = isNpgsql
                ? ("jsonb_extract_path_text", key)
                : ("json_extract", "$." + key);

            return new SqlFunctionExpression(
                function,
                arguments: [column, new SqlConstantExpression(argument, keyArg.TypeMapping)],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                type: typeof(string),
                typeMapping: keyArg.TypeMapping);
        });

        return modelBuilder;
    }
}

/// <summary>SQL-translatable access to a single extra property value (string-valued keys).</summary>
public static class SliceExtraProperties
{
    /// <summary>
    /// Returns the text value of extra property <paramref name="key"/>. Translates to the provider's
    /// JSON extraction in a query; the body is the in-memory/client fallback. <paramref name="key"/> is
    /// inlined as a literal so the JSON path is constant.
    /// </summary>
    public static string? JsonValue(ExtraPropertyDictionary properties, [NotParameterized] string key)
        => properties.TryGetValue(key, out var value) ? value?.ToString() : null;
}

public static class ExtraPropertyQueryExtensions
{
    /// <summary>Server-side filter: rows whose extra property <paramref name="key"/> equals <paramref name="value"/>.</summary>
    public static IQueryable<TEntity> WhereExtraProperty<TEntity>(this IQueryable<TEntity> source, string key, string value)
        where TEntity : class, IHasExtraProperties
        => source.Where(e => SliceExtraProperties.JsonValue(e.ExtraProperties, key) == value);
}
