using Slice.ApiVersioning;
using Slice.AspNetCore;
using Slice.AspNetCore.ConditionalRequests;
using Slice.AspNetCore.Http;
using Slice.AspNetCore.Hypermedia;
using Slice.AspNetCore.MinimalApi;
using Slice.Modularity;
using SliceMinimalApp;

var builder = WebApplication.CreateBuilder(args);

// Compose the app from the root module's dependency graph.
builder.Services.AddSliceModules<AppModule>(builder.Configuration);
builder.Services.AddSliceApiVersioning();
builder.Services.AddSliceOpenApi();

var app = builder.Build();

await app.Services.InitializeSliceModulesAsync();

app.UseSliceExceptionHandling();
app.UseSliceConditionalRequests();   // ETag/304 + If-Match/412 (inside the exception handler)

// Discover every ISliceEndpoint and map it onto one group wired with the Slice result mapper, a v1
// version set, HAL, and version-ETags. No controllers — pure minimal API.
var versionSet = app.NewSliceApiVersionSet(1.0);
app.MapSliceEndpoints(
    typeof(AppModule).Assembly,
    group => group.WithApiVersionSet(versionSet).AddHal().AddResourceVersion());

app.MapSliceOpenApi();   // /openapi/v1.json + interactive Scalar UI at /scalar/v1

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
