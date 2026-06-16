using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Slice.Modularity;

namespace Slice.Localization;

/// <summary>
/// Localization module: registers the localizer + default options and discovers
/// <see cref="ILocalizationContributor"/>s from feature modules. Web hosts call
/// <c>app.UseSliceLocalization()</c> to enable culture resolution (Accept-Language by default).
/// </summary>
public sealed class SliceLocalizationModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(new LocalizationOptions());
        context.Services.AddSliceConventions(typeof(SliceLocalizationModule).Assembly);
    }
}

public static class LocalizationApplicationBuilderExtensions
{
    /// <summary>
    /// Enables request localization. Supported cultures are derived from the registered
    /// <see cref="ILocalizationContributor"/>s; the default comes from <see cref="LocalizationOptions"/>.
    /// </summary>
    public static IApplicationBuilder UseSliceLocalization(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<LocalizationOptions>();
        var cultures = app.ApplicationServices
            .GetServices<ILocalizationContributor>()
            .Select(c => c.Culture)
            .Append(options.DefaultCulture)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(c => new CultureInfo(c))
            .ToList();

        var requestOptions = new RequestLocalizationOptions()
            .SetDefaultCulture(options.DefaultCulture);
        requestOptions.SupportedCultures = cultures;
        requestOptions.SupportedUICultures = cultures;

        return app.UseRequestLocalization(requestOptions);
    }
}
