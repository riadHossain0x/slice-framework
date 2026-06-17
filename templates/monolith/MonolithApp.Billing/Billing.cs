using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using MonolithApp.Contracts;

namespace MonolithApp.Billing;

// ── Persistence (EF · billing.db) ────────────────────────────────────────────
public sealed class Invoice
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
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

// ── Reacts to OrderPlacedEto from the Orders module (delivered via the outbox) ──
public sealed class CreateInvoiceOnOrderPlaced(
    BillingDbContext db,
    IDistributedEventBus bus,
    IGuidGenerator guids,
    ILogger<CreateInvoiceOnOrderPlaced> logger) : IDistributedEventHandler<OrderPlacedEto>
{
    private const decimal UnitPrice = 9.99m;

    public async Task HandleAsync(OrderPlacedEto @event, CancellationToken ct)
    {
        var invoice = new Invoice
        {
            Id = guids.Create(),
            OrderId = @event.OrderId,
            Amount = @event.Quantity * UnitPrice,
            CreatedAt = DateTime.UtcNow,
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BILLING invoice {Invoice} for order {Order}: {Amount:C}",
            invoice.Id, invoice.OrderId, invoice.Amount);

        await bus.PublishAsync(new InvoiceCreatedEto(invoice.Id, invoice.OrderId, invoice.Amount), ct);
    }
}

// ── Feature: list invoices ───────────────────────────────────────────────────
public sealed record InvoiceDto(Guid Id, Guid OrderId, decimal Amount);

public sealed record ListInvoicesQuery : IQuery<Result<IReadOnlyList<InvoiceDto>>>;

public sealed class ListInvoicesHandler(BillingDbContext db)
    : IQueryHandler<ListInvoicesQuery, Result<IReadOnlyList<InvoiceDto>>>
{
    public async Task<Result<IReadOnlyList<InvoiceDto>>> HandleAsync(ListInvoicesQuery query, CancellationToken ct)
    {
        var invoices = await db.Invoices.AsNoTracking().ToListAsync(ct);
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
