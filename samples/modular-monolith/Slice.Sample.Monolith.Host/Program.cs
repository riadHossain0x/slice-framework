using Slice.AspNetCore;
using Slice.AspNetCore.Http;
using Slice.AspNetCore.SignalR;
using Slice.Modularity;
using Slice.Sample.Monolith.Host;
using Slice.Sample.Monolith.Notifications;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSliceModules<HostModule>(builder.Configuration);
builder.Services.AddSliceOpenApi();

var app = builder.Build();
await app.Services.InitializeSliceModulesAsync();   // each module ensures its own database

app.UseSliceExceptionHandling();
app.MapControllers();
app.MapSliceOpenApi();   // /openapi/v1.json + interactive Scalar UI at /scalar/v1
app.MapSliceHub<NotificationsHub>("/hubs/notifications");   // real-time push from the Notifications module
app.MapGet("/", () => "Modular monolith: POST /api/orders → billing (LinqToDB) + inventory (Dapper) + notifications; "
    + "connect a SignalR client to /hubs/notifications to receive live 'notification' messages.");

app.Run();
