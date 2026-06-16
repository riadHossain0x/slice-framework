using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Slice.Domain.Events;
using Slice.Kafka;

namespace Slice.EventBus.Kafka;

public sealed class KafkaEventBusOptions
{
    public string Topic { get; set; } = "slice-events";
    public string GroupId { get; set; } = "slice-app";
}

/// <summary>Publishes distributed events to a Kafka topic (key = wire name; id carried in a header).</summary>
public sealed class KafkaEventPublisher(
    IKafkaProducerPool pool, KafkaEventBusOptions options, IDistributedEventTypeRegistry registry)
    : IDistributedEventPublisher
{
    public const string EventNameHeader = "slice-event-name";
    public const string MessageIdHeader = "slice-message-id";

    public async Task PublishAsync(IDistributedEvent @event, string messageId, CancellationToken ct = default)
    {
        var name = registry.GetName(@event.GetType());
        var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());

        var message = new Message<string, byte[]>
        {
            Key = name,
            Value = body,
            Headers =
            [
                new Header(EventNameHeader, Encoding.UTF8.GetBytes(name)),
                new Header(MessageIdHeader, Encoding.UTF8.GetBytes(messageId))
            ]
        };

        await pool.Get().ProduceAsync(options.Topic, message, ct);
    }
}

/// <summary>Consumes the topic and dispatches each record to local handlers (dedup via the inbox).</summary>
public sealed class KafkaConsumer(
    IKafkaConsumerFactory factory, KafkaEventBusOptions options,
    IServiceScopeFactory scopeFactory, ILogger<KafkaConsumer> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        // Confluent's Consume is blocking — run the poll loop off the host startup thread.
        => Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

    private void ConsumeLoop(CancellationToken ct)
    {
        using var consumer = factory.Create(options.GroupId);
        consumer.Subscribe(options.Topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "Kafka consume error on topic {Topic}", options.Topic);
                    continue;
                }

                if (result?.Message is null) continue;

                var eventName = Header(result, KafkaEventPublisher.EventNameHeader) ?? result.Message.Key;
                var messageId = Header(result, KafkaEventPublisher.MessageIdHeader) ?? Guid.NewGuid().ToString();

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IDistributedEventConsumer>();
                    dispatcher.ConsumeAsync(eventName, messageId, result.Message.Value, ct).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Kafka dispatch failed for {Event}", eventName);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            consumer.Close();
        }
    }

    private static string? Header(ConsumeResult<string, byte[]> result, string key)
        => result.Message.Headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}

public static class KafkaEventBusRegistration
{
    /// <summary>Routes distributed events through Kafka (publisher + consumer) instead of local loopback.</summary>
    public static IServiceCollection AddSliceKafkaEventBus(
        this IServiceCollection services,
        Action<KafkaConnectionOptions> connection,
        Action<KafkaEventBusOptions>? bus = null)
    {
        services.AddSliceKafka(connection);

        var options = new KafkaEventBusOptions();
        bus?.Invoke(options);
        services.AddSingleton(options);

        services.RemoveAll<IDistributedEventPublisher>();
        services.AddSingleton<IDistributedEventPublisher, KafkaEventPublisher>();
        services.AddHostedService<KafkaConsumer>();
        return services;
    }
}
