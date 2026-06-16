using Slice.Core.DependencyInjection;
using Slice.Localization;

namespace Slice.Sample.Crm.Localization;

public static class CrmKeys
{
    public const string Greeting = "Crm:Greeting";
}

public sealed class CrmEnglishLocalization : ILocalizationContributor, ISingletonDependency
{
    public string Culture => "en";
    public IReadOnlyDictionary<string, string> GetStrings() => new Dictionary<string, string>
    {
        [CrmKeys.Greeting] = "Hello from the CRM"
    };
}

public sealed class CrmSpanishLocalization : ILocalizationContributor, ISingletonDependency
{
    public string Culture => "es";
    public IReadOnlyDictionary<string, string> GetStrings() => new Dictionary<string, string>
    {
        [CrmKeys.Greeting] = "Hola desde el CRM"
    };
}
