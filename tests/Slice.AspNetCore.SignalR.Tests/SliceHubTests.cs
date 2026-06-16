using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Slice.AspNetCore.SignalR;

namespace Slice.AspNetCore.SignalR.Tests;

public sealed class EchoHub : SliceHub
{
    // Round-trips a message back to the caller; proves invoke + server-to-client push.
    public Task Ping(string message) => Clients.Caller.SendAsync("Pong", $"{message}-pong");
}

/// <summary>
/// Boots a real Kestrel host with a mapped <see cref="SliceHub"/> and connects a real SignalR
/// client over HTTP — end-to-end proof that hubs register, map and exchange messages.
/// </summary>
public sealed class SliceHubTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _baseUrl = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");   // bind a free port
        builder.Logging.ClearProviders();
        builder.Services.AddSliceSignalR();

        _app = builder.Build();
        _app.MapSliceHub<EchoHub>("/echo");
        await _app.StartAsync();
        _baseUrl = _app.Urls.First();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Client_invokes_hub_and_receives_server_push()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/echo")
            .Build();

        var pong = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<string>("Pong", message => pong.TrySetResult(message));

        await connection.StartAsync();
        await connection.InvokeAsync("Ping", "hello");

        var delivered = await Task.WhenAny(pong.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(pong.Task, delivered);
        Assert.Equal("hello-pong", await pong.Task);
    }
}
