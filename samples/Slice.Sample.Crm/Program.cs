using Slice.ApiVersioning;
using Slice.AspNetCore;
using Slice.AspNetCore.ConditionalRequests;
using Slice.AspNetCore.Http;
using Slice.AspNetCore.Hypermedia;
using Slice.AspNetCore.MinimalApi;
using Slice.Authentication;
using Slice.Localization;
using Slice.Modularity;
using Slice.MultiTenancy;
using Slice.Sample.Crm;

var builder = WebApplication.CreateBuilder(args);

// Compose the application from the CRM module's dependency graph.
builder.Services.AddSliceModules<CrmModule>(builder.Configuration);
builder.Services.AddSliceApiVersioning();
builder.Services.AddSliceOpenApi();   // OpenAPI document at /openapi/v1.json + Scalar UI at /scalar/v1

var app = builder.Build();

// Run module initialization (dependency-first).
await app.Services.InitializeSliceModulesAsync();

app.UseSliceExceptionHandling();
app.UseSliceConditionalRequests();   // ETag/304 + If-Match/412 (inside the exception handler)
app.UseSliceLocalization();
app.UseSliceAuthentication();
app.UseSliceMultiTenancy();
app.MapControllers();

// Minimal-API endpoints (discovered from the assembly) coexist with the controllers above, sharing the
// same handlers + pipeline. The group is wired with the Slice result mapper plus HAL + version-ETag and
// requires an authenticated caller.
app.MapSliceEndpoints(
    typeof(CrmModule).Assembly,
    group => group.RequireAuthorization().AddHal().AddResourceVersion());
app.MapSliceOpenApi();   // /openapi/v1.json + interactive Scalar UI at /scalar/v1

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
