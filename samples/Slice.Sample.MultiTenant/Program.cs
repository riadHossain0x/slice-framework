using Slice.AspNetCore;
using Slice.AspNetCore.Http;
using Slice.Modularity;
using Slice.MultiTenancy;
using Slice.Sample.MultiTenant;

// Migrations run at startup by default (convenient for dev). In production with many tenants / multiple
// replicas, set MultiTenant:RunMigrationsOnStartup=false and run the separate
// Slice.Sample.MultiTenant.Migrator job before rolling out the app.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSliceModules<TenantModule>(builder.Configuration);
builder.Services.AddSliceOpenApi();

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();   // creates the host + per-tenant databases

app.UseSliceExceptionHandling();
app.UseSliceMultiTenancy();   // resolves the tenant from the X-Tenant-Id header for the request
app.MapControllers();
app.MapSliceOpenApi();   // /openapi/v1.json + interactive Scalar UI at /scalar/v1
app.MapGet("/", () =>
    "Database-per-tenant. Send X-Tenant-Id: 11111111-1111-1111-1111-111111111111 (A) or " +
    "22222222-2222-2222-2222-222222222222 (B), then POST/GET /api/widgets.");

app.Run();
