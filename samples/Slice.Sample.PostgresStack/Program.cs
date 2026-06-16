using Slice.AspNetCore;
using Slice.AspNetCore.Http;
using Slice.Modularity;
using Slice.MultiTenancy;
using Slice.Sample.PostgresStack;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSliceModules<AppModule>(builder.Configuration);
builder.Services.AddSliceOpenApi();

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();   // schema init, EnsureCreated, vector collection

app.UseSliceExceptionHandling();
app.UseSliceMultiTenancy();
app.MapControllers();
app.MapSliceOpenApi();   // /openapi/v1.json + interactive Scalar UI at /scalar/v1
app.MapGet("/", () => "Slice on Postgres: POST /api/notes, GET /api/notes, POST /api/notes/search");

app.Run();

public partial class Program;
