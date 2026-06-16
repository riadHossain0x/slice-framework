using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Slice.AspNetCore.SignalR;
using Slice.EventBus;
using Slice.Modularity;
using Slice.Sample.Monolith.Contracts;

namespace Slice.Sample.Monolith.Notifications;

/// <summary>
/// The Notifications context — no database. It reacts to events from other modules and pushes them to
/// connected clients in real time over a SignalR hub (and logs them).
/// </summary>
[DependsOn(typeof(SliceSignalRModule))]
public sealed class NotificationsModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(NotificationsModule).Assembly;
        services.AddDistributedEvents(typeof(InvoiceCreatedEto).Assembly);
        services.AddDistributedEventHandlers(assembly);
    }
}

/// <summary>Push-only hub: clients connect and listen for the <c>notification</c> client method.</summary>
public sealed class NotificationsHub : SliceHub;

public sealed class NotifyOnInvoiceCreated(
    IHubContext<NotificationsHub> hub, ILogger<NotifyOnInvoiceCreated> logger)
    : IDistributedEventHandler<InvoiceCreatedEto>
{
    public async Task HandleAsync(InvoiceCreatedEto @event, CancellationToken ct)
    {
        var message = $"Invoice {@event.InvoiceId} ({@event.Amount:C}) is ready for order {@event.OrderId}";
        logger.LogInformation("NOTIFY customer: {Message}", message);

        // Broadcast to all clients (the sample has no auth/tenancy; with auth you'd target a
        // SliceHub group keyed by CurrentUserId/CurrentTenantId instead of Clients.All).
        await hub.Clients.All.SendAsync("notification", message, ct);
    }
}

public sealed class NotifyOnStockReserved(
    IHubContext<NotificationsHub> hub, ILogger<NotifyOnStockReserved> logger)
    : IDistributedEventHandler<StockReservedEto>
{
    public async Task HandleAsync(StockReservedEto @event, CancellationToken ct)
    {
        var message = $"{@event.Quantity}× {@event.Sku} reserved for order {@event.OrderId}";
        logger.LogInformation("NOTIFY customer: {Message}", message);

        await hub.Clients.All.SendAsync("notification", message, ct);
    }
}
