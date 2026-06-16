using Slice.Application;
using Slice.Modularity;

namespace Slice.Settings;

/// <summary>
/// Settings module: registers the definition manager, the value-provider chain
/// (global override → configuration → default), and the setting manager. Setting definition
/// providers are discovered from feature modules by convention.
/// </summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceSettingsModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceSettingsModule).Assembly);
}
