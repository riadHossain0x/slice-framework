using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Slice.Domain.Events;

namespace Slice.EventBus.RabbitMQ;

public sealed class RabbitMqOptions
{
    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672";
    public string Exchange { get; set; } = "slice.events";
    public string Queue { get; set; } = "slice.app";
}

/// <summary>Lazily-opened shared RabbitMQ connection.</summary>
public sealed class RabbitMqConnection(RabbitMqOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;
            var factory = new ConnectionFactory { Uri = new Uri(options.ConnectionString) };
            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}

/// <summary>Publishes distributed events to a topic exchange (routing key = event wire name).</summary>
public sealed class RabbitMqEventPublisher(
    RabbitMqConnection connection, RabbitMqOptions options, IDistributedEventTypeRegistry registry)
    : IDistributedEventPublisher
{
    public async Task PublishAsync(IDistributedEvent @event, string messageId, CancellationToken ct = default)
    {
        var name = registry.GetName(@event.GetType());
        var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());

        var conn = await connection.GetAsync(ct);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: ct);
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

        var props = new BasicProperties { MessageId = messageId, Persistent = true };
        await channel.BasicPublishAsync(options.Exchange, routingKey: name, mandatory: false, basicProperties: props, body: body, cancellationToken: ct);
    }
}

/// <summary>Consumes from the app queue and dispatches each message to local handlers (with dedup).</summary>
public sealed class RabbitMqConsumer(
    RabbitMqConnection connection, RabbitMqOptions options,
    IServiceScopeFactory scopeFactory, ILogger<RabbitMqConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conn = await connection.GetAsync(stoppingToken);
        var channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(options.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.Queue, options.Exchange, routingKey: "#", cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var messageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDistributedEventConsumer>();
                await dispatcher.ConsumeAsync(ea.RoutingKey, messageId, ea.Body.ToArray(), stoppingToken);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RabbitMQ consume failed for {Routing}", ea.RoutingKey);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(options.Queue, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }
}

public static class RabbitMqRegistration
{
    /// <summary>Routes distributed events through RabbitMQ (publisher + consumer) instead of local loopback.</summary>
    public static IServiceCollection AddSliceRabbitMq(this IServiceCollection services, Action<RabbitMqOptions> configure)
    {
        var options = new RabbitMqOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton<RabbitMqConnection>();

        services.RemoveAll<IDistributedEventPublisher>();
        services.AddSingleton<IDistributedEventPublisher, RabbitMqEventPublisher>();
        services.AddHostedService<RabbitMqConsumer>();
        return services;
    }
}
