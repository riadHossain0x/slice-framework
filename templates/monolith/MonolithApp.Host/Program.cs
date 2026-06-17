using Slice.AspNetCore;
using Slice.AspNetCore.Http;
using Slice.Modularity;
using MonolithApp.Host;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSliceModules<HostModule>(builder.Configuration);
builder.Services.AddSliceOpenApi();

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();   // each module ensures its own database

app.UseSliceExceptionHandling();
app.MapControllers();
app.MapSliceOpenApi();   // /openapi/v1.json + interactive Scalar UI at /scalar/v1
app.MapGet("/", () => "Modular monolith. POST /api/orders → Billing reacts (in-process event) → GET /api/invoices.");

app.Run();
