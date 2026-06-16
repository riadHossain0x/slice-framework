using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.Domain.Events;
using Slice.EventBus;
using Slice.EventBus.RabbitMQ;
using Slice.Modularity;
using Testcontainers.RabbitMq;

namespace Slice.EventBus.RabbitMQ.Tests;

[DistributedEventName("test.ping")]
public sealed record PingEto(string Message) : IDistributedEvent;

public sealed class PingReceiver
{
    public TaskCompletionSource<string> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class PingHandler(PingReceiver receiver) : IDistributedEventHandler<PingEto>
{
    public Task HandleAsync(PingEto @event, CancellationToken ct)
    {
        receiver.Received.TrySetResult(@event.Message);
        return Task.CompletedTask;
    }
}

public sealed class RabbitMqTransportTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder().Build();

    public Task InitializeAsync() => _rabbit.StartAsync();
    public Task DisposeAsync() => _rabbit.DisposeAsync().AsTask();

    [Fact]
    public async Task Event_published_to_RabbitMQ_is_delivered_to_a_handler()
    {
        var receiver = new PingReceiver();

        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddSingleton(receiver);
        services.AddSliceConventions(typeof(DistributedEventConsumer).Assembly); // bus, consumer, registry, inbox, local publisher
        services.AddDistributedEvents(typeof(PingEto).Assembly);
        services.AddTransient<IDistributedEventHandler<PingEto>, PingHandler>();
        services.AddSliceRabbitMq(o => o.ConnectionString = _rabbit.GetConnectionString());

        using var host = builder.Build();
        await host.StartAsync();                          // starts the RabbitMqConsumer
        await Task.Delay(500);                            // let the consumer bind the queue

        var publisher = host.Services.GetRequiredService<IDistributedEventPublisher>();
        Assert.IsType<RabbitMqEventPublisher>(publisher); // the broker publisher replaced local loopback
        await publisher.PublishAsync(new PingEto("hello-over-rabbit"), Guid.NewGuid().ToString());

        var delivered = await Task.WhenAny(receiver.Received.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(receiver.Received.Task, delivered);   // handler fired before timeout
        Assert.Equal("hello-over-rabbit", await receiver.Received.Task);

        await host.StopAsync();
    }
}
