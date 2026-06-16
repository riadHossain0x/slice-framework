using Slice.ApiVersioning;
using Slice.AspNetCore;
using Slice.AspNetCore.Http;
using Slice.Authentication;
using Slice.Localization;
using Slice.Modularity;
using Slice.MultiTenancy;
using SliceApp;

var builder = WebApplication.CreateBuilder(args);

// Compose the application from the root module's dependency graph.
builder.Services.AddSliceModules<AppModule>(builder.Configuration);
builder.Services.AddSliceApiVersioning();
builder.Services.AddSliceOpenApi();

var app = builder.Build();

// Run module initialization (dependency-first): EnsureCreated, seeding, etc.
await app.Services.InitializeSliceModulesAsync();

app.UseSliceExceptionHandling();
app.UseSliceLocalization();
app.UseSliceAuthentication();
app.UseSliceMultiTenancy();
app.MapControllers();
app.MapSliceOpenApi();   // OpenAPI document at /openapi/v1.json + interactive Scalar UI at /scalar/v1

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
