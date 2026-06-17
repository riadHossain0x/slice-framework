using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.DistributedLocking.Redis;
using Slice.Management;
using Slice.Modularity;
using Slice.Sample.MultiTenant;

// A standalone migration job: composes the SAME module graph as the web app (TenantModule), then applies
// EF migrations to the host database + every tenant in the SliceTenants registry — and exits. Run this as
// a deploy/CI step (or a Kubernetes Job) before rolling out the app, with the app started using
// MultiTenant:RunMigrationsOnStartup=false so it never migrates in-process.

var builder = Host.CreateApplicationBuilder(args);

// We migrate explicitly below, so the module's startup migration is disabled (it still seeds the registry).
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["MultiTenant:RunMigrationsOnStartup"] = "false",
});
builder.Services.AddSliceModules<TenantModule>(builder.Configuration);

// Real single-runner coordination across concurrent migration jobs (optional): set ConnectionStrings:Redis
// (env: ConnectionStrings__Redis). Must be registered AFTER AddSliceModules so its RemoveAll<IDistributedLock>()
// overrides the Core no-op default. Without it, the no-op lock is used and the job migrates normally.
var redis = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis))
    builder.Services.AddSliceRedisDistributedLock(redis);

using var host = builder.Build();
await host.Services.InitializeSliceModulesAsync();   // seeds the registry; startup migration is skipped

using var scope = host.Services.CreateScope();
var migrator = scope.ServiceProvider.GetRequiredService<ITenantDatabaseMigrator>();

var report = await migrator.MigrateAllAsync(new TenantMigrationOptions
{
    MaxDegreeOfParallelism = 4,    // migrate tenants concurrently (tune to your DB capacity)
    ContinueOnError = true,        // attempt every tenant; collect failures instead of aborting the fleet
    UseDistributedLock = true,     // single-runner guard (no-op unless a real IDistributedLock is registered)
});

if (report.LockNotAcquired)
{
    Console.WriteLine("Another migration run holds the lock; nothing to do.");
    return 0;
}

foreach (var r in report.Results)
    Console.WriteLine($"{(r.Migrated ? "OK  " : "FAIL")} {r.TenantId?.ToString() ?? "(host)"}{(r.Error is null ? "" : " — " + r.Error)}");

Console.WriteLine($"Migrated {report.Succeeded}, failed {report.Failed}.");
return report.Failed == 0 ? 0 : 1;   // non-zero exit fails the CI/job step
