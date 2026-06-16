using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.Domain.Events;
using Slice.EventBus;
using Slice.EventBus.Kafka;
using Slice.Modularity;
using Testcontainers.Kafka;

namespace Slice.EventBus.Kafka.Tests;

[DistributedEventName("test.kafka-ping")]
public sealed record KafkaPing(string Message) : IDistributedEvent;

public sealed class KafkaPingReceiver
{
    public TaskCompletionSource<string> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class KafkaPingHandler(KafkaPingReceiver receiver) : IDistributedEventHandler<KafkaPing>
{
    public Task HandleAsync(KafkaPing @event, CancellationToken ct)
    {
        receiver.Received.TrySetResult(@event.Message);
        return Task.CompletedTask;
    }
}

/// <summary>End-to-end Kafka transport test against a real broker (Testcontainers): an event
/// published to Kafka is consumed by the background consumer and reaches a local handler.</summary>
public sealed class KafkaTransportTests : IAsyncLifetime
{
    // Pin a multi-arch tag (the default cp-kafka tag has no arm64 manifest).
    private readonly KafkaContainer _kafka = new KafkaBuilder().WithImage("confluentinc/cp-kafka:7.7.1").Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    [Fact]
    public async Task Event_published_to_Kafka_is_delivered_to_a_handler()
    {
        var receiver = new KafkaPingReceiver();

        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddSingleton(receiver);
        services.AddSliceConventions(typeof(DistributedEventConsumer).Assembly); // bus, consumer, registry, inbox
        services.AddDistributedEvents(typeof(KafkaPing).Assembly);
        services.AddTransient<IDistributedEventHandler<KafkaPing>, KafkaPingHandler>();
        services.AddSliceKafkaEventBus(
            connection: o => o.BootstrapServers = _kafka.GetBootstrapAddress(),
            bus: o => { o.Topic = "slice-test-events"; o.GroupId = "slice-test-group"; });

        using var host = builder.Build();
        await host.StartAsync();   // starts the KafkaConsumer (subscribes; Earliest offset)

        var publisher = host.Services.GetRequiredService<IDistributedEventPublisher>();
        Assert.IsType<KafkaEventPublisher>(publisher);   // broker publisher replaced local loopback
        await publisher.PublishAsync(new KafkaPing("hello-over-kafka"), Guid.NewGuid().ToString());

        // Kafka consumer-group rebalancing can take several seconds; Earliest offset guards ordering.
        var delivered = await Task.WhenAny(receiver.Received.Task, Task.Delay(TimeSpan.FromSeconds(45)));
        Assert.Same(receiver.Received.Task, delivered);
        Assert.Equal("hello-over-kafka", await receiver.Received.Task);

        await host.StopAsync();
    }
}
