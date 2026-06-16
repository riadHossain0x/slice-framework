using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Context;
using Serilog.Sinks.InMemory;
using Slice.Serilog;

namespace Slice.Serilog.Tests;

/// <summary>
/// Verifies UseSliceSerilog wires Serilog as the logging provider and that LogContext enrichment
/// (the mechanism the request middleware uses for TenantId/UserId) reaches the emitted events.
/// </summary>
public sealed class SliceSerilogTests
{
    [Fact]
    public void UseSliceSerilog_routes_logs_to_serilog_and_enriches_from_log_context()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseSliceSerilog(lc => lc.MinimumLevel.Verbose().WriteTo.InMemory());
        using var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILogger<SliceSerilogTests>>();
        var tenantId = Guid.NewGuid();

        using (LogContext.PushProperty("TenantId", tenantId))
            logger.LogInformation("processed {Count} leads", 3);

        // Note: don't Log.CloseAndFlush() before asserting — disposing the logger clears the
        // in-memory sink. The InMemory sink is synchronous, so the event is already captured.
        var evt = Assert.Single(InMemorySink.Instance.LogEvents);
        Assert.Equal("processed {Count} leads", evt.MessageTemplate.Text);
        Assert.Equal("3", evt.Properties["Count"].ToString());
        Assert.Contains("TenantId", evt.Properties.Keys);                 // enriched from LogContext
        Assert.Equal(tenantId.ToString(), evt.Properties["TenantId"].ToString().Trim('"'));
    }
}
