using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slice.Application;
using Slice.Modularity;

namespace Slice.Emailing;

/// <summary>Emailing module: registers the default <see cref="NullEmailSender"/>.</summary>
[DependsOn(typeof(SliceApplicationModule))]
public sealed class SliceEmailingModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
        => context.Services.AddSliceConventions(typeof(SliceEmailingModule).Assembly);
}

public static class SmtpEmailRegistration
{
    /// <summary>Swaps the null email sender for an SMTP-backed one.</summary>
    public static IServiceCollection AddSliceSmtpEmailSender(this IServiceCollection services, Action<SmtpEmailOptions> configure)
    {
        var options = new SmtpEmailOptions();
        configure(options);
        services.RemoveAll<IEmailSender>();
        services.AddSingleton(options);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
