using Slice.Core.DependencyInjection;

namespace Slice.Localization;

/// <summary>
/// Contributes localized strings for one culture (e.g. "en", "es"). Multiple contributors for the
/// same culture are merged. Discovered from feature modules by convention.
/// </summary>
public interface ILocalizationContributor
{
    string Culture { get; }
    IReadOnlyDictionary<string, string> GetStrings();
}

/// <summary>Resolves a localized string for the current UI culture.</summary>
public interface ISliceLocalizer
{
    /// <summary>Localized value for <paramref name="key"/>, falling back to the default culture then the key.</summary>
    string this[string key] { get; }

    /// <summary>Localized + <see cref="string.Format(string, object?[])"/>-formatted value.</summary>
    string Format(string key, params object[] args);
}
