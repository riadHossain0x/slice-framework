using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.Domain.Events;
using Slice.EventBus;
using Slice.EventBus.Postgres;
using Slice.Modularity;

namespace Slice.Postgres.Tests;

[DistributedEventName("test.pg-ping")]
public sealed record PgPing(string Message) : IDistributedEvent;

public sealed class PgPingReceiver
{
    public TaskCompletionSource<string> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class PgPingHandler(PgPingReceiver receiver) : IDistributedEventHandler<PgPing>
{
    public Task HandleAsync(PgPing @event, CancellationToken ct)
    {
        receiver.Received.TrySetResult(@event.Message);
        return Task.CompletedTask;
    }
}

[Collection("postgres")]
public sealed class PostgresEventBusTests(PostgresFixture fx)
{
    [Fact]
    public async Task Event_published_to_Postgres_is_delivered_to_a_handler()
    {
        var receiver = new PgPingReceiver();

        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddSingleton(receiver);
        services.AddSliceConventions(typeof(DistributedEventConsumer).Assembly); // bus, consumer, registry, inbox
        services.AddDistributedEvents(typeof(PgPing).Assembly);
        services.AddTransient<IDistributedEventHandler<PgPing>, PgPingHandler>();
        services.AddSlicePostgresEventBus(fx.ConnectionString);

        using var host = builder.Build();
        await host.StartAsync();   // schema init + LISTEN consumer

        var publisher = host.Services.GetRequiredService<IDistributedEventPublisher>();
        Assert.IsType<PostgresEventPublisher>(publisher);   // broker publisher replaced local loopback
        await publisher.PublishAsync(new PgPing("hello-over-postgres"), Guid.NewGuid().ToString());

        var delivered = await Task.WhenAny(receiver.Received.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(receiver.Received.Task, delivered);     // NOTIFY-driven, well under the timeout
        Assert.Equal("hello-over-postgres", await receiver.Received.Task);

        await host.StopAsync();
    }
}
