using Slice.Settings;

namespace Slice.Sample.Crm.Settings;

public static class CrmSettings
{
    public const string AutoArchiveDays = "Crm.AutoArchiveDays";
}

public sealed class CrmSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
        => context.Add(new SettingDefinition(
            CrmSettings.AutoArchiveDays,
            defaultValue: "30",
            displayName: "Auto-archive leads after N days",
            isVisibleToClients: true));
}
