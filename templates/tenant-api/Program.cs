using Slice.AspNetCore;
using Slice.AspNetCore.Http;
using Slice.Modularity;
using Slice.MultiTenancy;
using TenantApp;

// Tenant databases are migrated at host startup when MultiTenant:RunMigrationsOnStartup is true (the
// default). If you scaffolded with --migrations job, that flag is set false in appsettings.json and the
// generated TenantApp.Migrator runs the migrations as a separate step (decoupling migration from serving;
// recommended for many tenants / multiple replicas).
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSliceModules<TenantModule>(builder.Configuration);
builder.Services.AddSliceOpenApi();

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();   // creates the registry + per-tenant databases

app.UseSliceExceptionHandling();
app.UseSliceMultiTenancy();   // resolves the tenant from the X-Tenant-Id header for the request
app.MapControllers();
app.MapSliceOpenApi();   // /openapi/v1.json + interactive Scalar UI at /scalar/v1
app.MapGet("/", () =>
    "Database-per-tenant. Seeded demo tenants: 11111111-1111-1111-1111-111111111111 (A) / " +
    "22222222-2222-2222-2222-222222222222 (B). Send X-Tenant-Id, then POST/GET /api/widgets; " +
    "POST /api/tenants to onboard a new tenant.");

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
