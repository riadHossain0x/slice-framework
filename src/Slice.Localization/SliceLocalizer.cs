using System.Globalization;
using Slice.Core.DependencyInjection;

namespace Slice.Localization;

public sealed class LocalizationOptions
{
    public string DefaultCulture { get; set; } = "en";
}

/// <summary>
/// Merges all <see cref="ILocalizationContributor"/>s into a per-culture lookup and resolves
/// strings for <see cref="CultureInfo.CurrentUICulture"/>, falling back to the culture's parent,
/// the default culture, then the key itself.
/// </summary>
public sealed class SliceLocalizer : ISliceLocalizer, ISingletonDependency
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _byCulture;
    private readonly string _defaultCulture;

    public SliceLocalizer(IEnumerable<ILocalizationContributor> contributors, LocalizationOptions options)
    {
        _defaultCulture = options.DefaultCulture;
        _byCulture = contributors
            .GroupBy(c => c.Culture, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, string>)g
                    .SelectMany(c => c.GetStrings())
                    .GroupBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => kv.Last().Value, StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);
    }

    public string this[string key]
    {
        get
        {
            var culture = CultureInfo.CurrentUICulture;
            // current culture → parent (e.g. es-MX → es) → default culture
            foreach (var name in new[] { culture.Name, culture.TwoLetterISOLanguageName, _defaultCulture })
                if (!string.IsNullOrEmpty(name)
                    && _byCulture.TryGetValue(name, out var strings)
                    && strings.TryGetValue(key, out var value))
                    return value;
            return key;
        }
    }

    public string Format(string key, params object[] args) => string.Format(this[key], args);
}
