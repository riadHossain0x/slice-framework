using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.Dapper;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using Slice.Sample.Monolith.Contracts;

namespace Slice.Sample.Monolith.Inventory;

// A simple stock row. EF maps the table (so EnsureCreated provisions + seeds it); Dapper reads/writes it.
public sealed class StockItem
{
    public string Sku { get; set; } = string.Empty;
    public int Available { get; set; }
    public int Reserved { get; set; }
}

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ICurrentTenant t, IDataFilter f)
    : SliceDbContext(options, t, f)
{
    public DbSet<StockItem> Stock => Set<StockItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StockItem>(e =>
        {
            e.ToTable("Stock");
            e.HasKey(x => x.Sku);
        });
        base.OnModelCreating(b);
    }
}

// ── Reacts to OrderPlacedEto — reserves stock via raw SQL (Dapper) ──
public sealed class ReserveStockOnOrderPlaced(
    IDapperExecutor<InventoryDbContext> dapper,
    IDistributedEventBus bus,
    ILogger<ReserveStockOnOrderPlaced> logger) : IDistributedEventHandler<OrderPlacedEto>
{
    public async Task HandleAsync(OrderPlacedEto @event, CancellationToken ct)
    {
        await dapper.ExecuteAsync(
            "UPDATE Stock SET Reserved = Reserved + @qty WHERE Sku = @sku",
            new { qty = @event.Quantity, sku = @event.Sku }, ct);

        logger.LogInformation("INVENTORY reserved {Qty} of {Sku} for order {Order} (via Dapper)",
            @event.Quantity, @event.Sku, @event.OrderId);

        await bus.PublishAsync(new StockReservedEto(@event.OrderId, @event.Sku, @event.Quantity), ct);
    }
}

// ── Query stock via Dapper ──
public sealed record StockDto(string Sku, int Available, int Reserved);

public sealed record GetStockQuery(string Sku) : IQuery<Result<StockDto>>;

public sealed class GetStockHandler(IDapperExecutor<InventoryDbContext> dapper)
    : IQueryHandler<GetStockQuery, Result<StockDto>>
{
    public async Task<Result<StockDto>> HandleAsync(GetStockQuery query, CancellationToken ct)
    {
        var item = await dapper.QueryFirstOrDefaultAsync<StockItem>(
            "SELECT Sku, Available, Reserved FROM Stock WHERE Sku = @sku", new { sku = query.Sku }, ct);

        return item is null
            ? Result<StockDto>.Failure(Error.NotFound("Stock.NotFound", $"No stock for '{query.Sku}'."))
            : Result<StockDto>.Success(new StockDto(item.Sku, item.Available, item.Reserved));
    }
}

[Route("api/stock")]
public sealed class StockController : SliceController
{
    [HttpGet("{sku}")]
    public Task<IActionResult> Get(string sku, CancellationToken ct) => SendAsync(new GetStockQuery(sku), ct);
}
