using LinqToDB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using Slice.Features;
using Slice.LinqToDB;
using Slice.Sample.Monolith.Contracts;

namespace Slice.Sample.Monolith.Billing;

/// <summary>The feature that gates the whole Billing module (enabled by default).</summary>
public static class BillingFeatures
{
    public const string Billing = "Billing";
}

public sealed class BillingFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
        => context.Add(new FeatureDefinition(BillingFeatures.Billing, defaultValue: "true", displayName: "Billing module"));
}

// Invoice is a downstream projection (not a rich aggregate). It carries LinqToDB mapping attributes so
// LinqToDB reads/writes it; EF maps the same table (below) only so EnsureCreated provisions it.
[global::LinqToDB.Mapping.Table("Invoices")]
public sealed class Invoice
{
    [global::LinqToDB.Mapping.PrimaryKey] public Guid Id { get; set; }
    [global::LinqToDB.Mapping.Column] public Guid OrderId { get; set; }
    [global::LinqToDB.Mapping.Column] public decimal Amount { get; set; }
    [global::LinqToDB.Mapping.Column] public DateTime CreatedAt { get; set; }
}

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, ICurrentTenant t, IDataFilter f)
    : SliceDbContext(options, t, f)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Invoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        });
        base.OnModelCreating(b);
    }
}

// ── Reacts to OrderPlacedEto from the Sales module — writes the invoice via LinqToDB ──
public sealed class CreateInvoiceOnOrderPlaced(
    ISliceDataConnectionFactory<BillingDbContext> linq2db,
    IDistributedEventBus bus,
    IGuidGenerator guids,
    IFeatureChecker featureChecker,
    ILogger<CreateInvoiceOnOrderPlaced> logger) : IDistributedEventHandler<OrderPlacedEto>
{
    private const decimal UnitPrice = 9.99m;

    public async Task HandleAsync(OrderPlacedEto @event, CancellationToken ct)
    {
        // Distributed-event handlers bypass the mediator pipeline, so the module/attribute feature gates
        // don't apply here — guard the handler explicitly.
        if (!await featureChecker.IsEnabledAsync(BillingFeatures.Billing))
        {
            logger.LogInformation("BILLING disabled — skipping invoice for order {Order}", @event.OrderId);
            return;
        }

        var invoice = new Invoice
        {
            Id = guids.Create(),
            OrderId = @event.OrderId,
            Amount = @event.Quantity * UnitPrice,
            CreatedAt = DateTime.UtcNow
        };

        using (var dc = await linq2db.CreateAsync(ct))
            await dc.InsertAsync(invoice, token: ct);     // LinqToDB write, on BillingDbContext's connection

        logger.LogInformation("BILLING invoice {Invoice} for order {Order}: {Amount:C} (via LinqToDB)",
            invoice.Id, invoice.OrderId, invoice.Amount);

        await bus.PublishAsync(new InvoiceCreatedEto(invoice.Id, invoice.OrderId, invoice.Amount), ct);
    }
}

// ── Query invoices via LinqToDB ──
public sealed record InvoiceDto(Guid Id, Guid OrderId, decimal Amount);

public sealed record ListInvoicesQuery : IQuery<Result<IReadOnlyList<InvoiceDto>>>;

public sealed class ListInvoicesHandler(ISliceDataConnectionFactory<BillingDbContext> linq2db)
    : IQueryHandler<ListInvoicesQuery, Result<IReadOnlyList<InvoiceDto>>>
{
    public async Task<Result<IReadOnlyList<InvoiceDto>>> HandleAsync(ListInvoicesQuery query, CancellationToken ct)
    {
        using var dc = await linq2db.CreateAsync(ct);
        var invoices = await global::LinqToDB.AsyncExtensions.ToListAsync(dc.GetTable<Invoice>(), ct);
        IReadOnlyList<InvoiceDto> dtos = invoices.Select(i => new InvoiceDto(i.Id, i.OrderId, i.Amount)).ToList();
        return Result<IReadOnlyList<InvoiceDto>>.Success(dtos);
    }
}

[Route("api/invoices")]
public sealed class InvoicesController : SliceController
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => SendAsync(new ListInvoicesQuery(), ct);
}
