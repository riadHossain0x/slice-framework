using Microsoft.Extensions.Hosting;
using Slice.Modularity;
using SliceWorker;

// A headless host (no web server): compose the module graph, run module initialization, then run the
// Generic Host so the registered background workers tick. Stop with Ctrl+C / SIGTERM.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSliceModules<WorkerModule>(builder.Configuration);

using var host = builder.Build();
await host.Services.InitializeSliceModulesAsync();   // runs each module's initialization
await host.RunAsync();                               // starts the BackgroundWorkerManager hosted service
