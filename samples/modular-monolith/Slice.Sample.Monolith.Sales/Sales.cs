using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.Domain;
using Slice.Domain.Entities;
using Slice.Domain.Repositories;
using Slice.EntityFrameworkCore;
using Slice.EntityFrameworkCore.ExtraProperties;
using Slice.Sample.Monolith.Contracts;

namespace Slice.Sample.Monolith.Sales;

// ── Domain ──────────────────────────────────────────────────────────────────
public sealed class Order : AggregateRoot<Guid>
{
    private Order() { }

    public Order(Guid id, string customer, string sku, int quantity) : base(id)
    {
        Customer = customer;
        Sku = sku;
        Quantity = quantity;
        // Raised via the transactional outbox when the order is saved.
        AddDistributedEvent(new OrderPlacedEto(id, customer, sku, quantity));
    }

    public string Customer { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
}

public interface IOrderRepository : IRepository<Order, Guid>;

// ── Persistence (EF · sales.db) ─────────────────────────────────────────────
public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options, ICurrentTenant t, IDataFilter f)
    : SliceDbContext(options, t, f)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.Property(x => x.Customer).IsRequired();
        });
        base.OnModelCreating(b);
    }
}

public sealed class EfOrderRepository(SalesDbContext db) : EfRepository<SalesDbContext, Order, Guid>(db), IOrderRepository;

// ── Feature: place an order ─────────────────────────────────────────────────
// Channel / GiftNote are ad-hoc metadata — stored in the order's ExtraProperties JSON column, no
// schema change needed.
public sealed record PlaceOrderCommand(string Customer, string Sku, int Quantity, string? Channel = null, string? GiftNote = null)
    : ICommand<Result<Guid>>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.Customer).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class PlaceOrderHandler(IOrderRepository repository, IGuidGenerator guids)
    : ICommandHandler<PlaceOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(PlaceOrderCommand command, CancellationToken ct)
    {
        var order = new Order(guids.Create(), command.Customer, command.Sku, command.Quantity);

        // Ad-hoc, schema-less metadata via ExtraProperties (persisted as a JSON column).
        if (command.Channel is not null) order.SetProperty("channel", command.Channel);
        if (command.GiftNote is not null) order.SetProperty("giftNote", command.GiftNote);

        await repository.InsertAsync(order, autoSave: false, ct);   // UoW commits → outbox → bus
        return Result<Guid>.Success(order.Id);
    }
}

// ── Feature: list orders (surfacing an extra property) ──────────────────────
public sealed record OrderDto(Guid Id, string Customer, string Sku, int Quantity, string? Channel);

public sealed record ListOrdersQuery : IQuery<Result<IReadOnlyList<OrderDto>>>;

public sealed class ListOrdersHandler(IOrderRepository repository)
    : IQueryHandler<ListOrdersQuery, Result<IReadOnlyList<OrderDto>>>
{
    public async Task<Result<IReadOnlyList<OrderDto>>> HandleAsync(ListOrdersQuery query, CancellationToken ct)
    {
        var orders = await repository.GetListAsync(specification: null, ct);
        IReadOnlyList<OrderDto> dtos = orders
            .Select(o => new OrderDto(o.Id, o.Customer, o.Sku, o.Quantity, o.GetProperty<string>("channel")))
            .ToList();
        return Result<IReadOnlyList<OrderDto>>.Success(dtos);
    }
}

// ── Feature: filter orders by an extra property (server-side json_extract) ──
public sealed record ListOrdersByChannelQuery(string Channel) : IQuery<Result<IReadOnlyList<OrderDto>>>;

public sealed class ListOrdersByChannelHandler(IOrderRepository repository)
    : IQueryHandler<ListOrdersByChannelQuery, Result<IReadOnlyList<OrderDto>>>
{
    public async Task<Result<IReadOnlyList<OrderDto>>> HandleAsync(ListOrdersByChannelQuery query, CancellationToken ct)
    {
        var queryable = await repository.GetQueryableAsync();
        var orders = await queryable.WhereExtraProperty("channel", query.Channel).ToListAsync(ct);
        IReadOnlyList<OrderDto> dtos = orders
            .Select(o => new OrderDto(o.Id, o.Customer, o.Sku, o.Quantity, o.GetProperty<string>("channel")))
            .ToList();
        return Result<IReadOnlyList<OrderDto>>.Success(dtos);
    }
}

[Route("api/orders")]
public sealed class OrdersController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Place([FromBody] PlaceOrderCommand command, CancellationToken ct) => SendAsync(command, ct);

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => SendAsync(new ListOrdersQuery(), ct);

    [HttpGet("by-channel/{channel}")]
    public Task<IActionResult> ByChannel(string channel, CancellationToken ct) => SendAsync(new ListOrdersByChannelQuery(channel), ct);
}
