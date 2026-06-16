using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Slice.Domain.Events;

namespace Slice.EventBus.AzureServiceBus;

public sealed class AzureServiceBusOptions
{
    public required string ConnectionString { get; set; }
    public string Topic { get; set; } = "slice-events";
    public string Subscription { get; set; } = "slice-app";
}

/// <summary>Publishes distributed events to an Azure Service Bus topic (Subject = event wire name).</summary>
public sealed class AzureServiceBusEventPublisher(ServiceBusClient client, AzureServiceBusOptions options, IDistributedEventTypeRegistry registry)
    : IDistributedEventPublisher
{
    public async Task PublishAsync(IDistributedEvent @event, string messageId, CancellationToken ct = default)
    {
        await using var sender = client.CreateSender(options.Topic);
        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType()))
        {
            MessageId = messageId,
            Subject = registry.GetName(@event.GetType())
        };
        await sender.SendMessageAsync(message, ct);
    }
}

/// <summary>Processes a topic subscription and dispatches each message to local handlers (with dedup).</summary>
public sealed class AzureServiceBusConsumer(
    ServiceBusClient client, AzureServiceBusOptions options,
    IServiceScopeFactory scopeFactory, ILogger<AzureServiceBusConsumer> logger) : IHostedService
{
    private ServiceBusProcessor? _processor;

    public async Task StartAsync(CancellationToken ct)
    {
        _processor = client.CreateProcessor(options.Topic, options.Subscription, new ServiceBusProcessorOptions());
        _processor.ProcessMessageAsync += async args =>
        {
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDistributedEventConsumer>();
            await dispatcher.ConsumeAsync(args.Message.Subject, args.Message.MessageId, args.Message.Body.ToArray(), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Azure Service Bus processing error");
            return Task.CompletedTask;
        };
        await _processor.StartProcessingAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(ct);
            await _processor.DisposeAsync();
        }
    }
}

public static class AzureServiceBusRegistration
{
    /// <summary>Routes distributed events through Azure Service Bus instead of local loopback.</summary>
    public static IServiceCollection AddSliceAzureServiceBus(this IServiceCollection services, Action<AzureServiceBusOptions> configure)
    {
        var options = new AzureServiceBusOptions { ConnectionString = "" };
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton(_ => new ServiceBusClient(options.ConnectionString));

        services.RemoveAll<IDistributedEventPublisher>();
        services.AddSingleton<IDistributedEventPublisher, AzureServiceBusEventPublisher>();
        services.AddHostedService<AzureServiceBusConsumer>();
        return services;
    }
}
