using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Context;
using Slice.Core.Ambient;

namespace Slice.Serilog;

public static class SliceSerilogExtensions
{
    /// <summary>
    /// Configures Serilog as the logging provider, reading sinks/levels from configuration, pulling
    /// services from DI, and enriching from <see cref="LogContext"/>. Call on the host builder.
    /// </summary>
    public static T UseSliceSerilog<T>(this T builder, Action<LoggerConfiguration>? configure = null)
        where T : IHostApplicationBuilder
    {
        var loggerConfiguration = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext();
        configure?.Invoke(loggerConfiguration);

        Log.Logger = loggerConfiguration.CreateLogger();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);
        return builder;
    }

    /// <summary>
    /// Adds Serilog HTTP request logging plus per-request enrichment: every log written during a
    /// request carries the current <c>TenantId</c> and <c>UserId</c>. Place early in the pipeline.
    /// </summary>
    public static IApplicationBuilder UseSliceSerilogRequestLogging(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var tenant = context.RequestServices.GetService<ICurrentTenant>();
            var user = context.RequestServices.GetService<ICurrentUser>();
            using (LogContext.PushProperty("TenantId", tenant?.Id))
            using (LogContext.PushProperty("UserId", user?.Id))
            {
                await next();
            }
        });
        return app.UseSerilogRequestLogging();
    }
}
