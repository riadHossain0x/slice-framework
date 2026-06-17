using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.Domain.Entities;
using Slice.Domain.Repositories;
using Slice.EntityFrameworkCore;
using MonolithApp.Contracts;

namespace MonolithApp.Orders;

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

// ── Persistence (EF · orders.db) ────────────────────────────────────────────
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options, ICurrentTenant t, IDataFilter f)
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

public sealed class EfOrderRepository(OrdersDbContext db) : EfRepository<OrdersDbContext, Order, Guid>(db), IOrderRepository;

// ── Feature: place an order ─────────────────────────────────────────────────
public sealed record PlaceOrderCommand(string Customer, string Sku, int Quantity) : ICommand<Result<Guid>>;

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
        await repository.InsertAsync(order, autoSave: false, ct);   // UoW commits → outbox → bus → Billing reacts
        return Result<Guid>.Success(order.Id);
    }
}

// ── Feature: list orders ─────────────────────────────────────────────────────
public sealed record OrderDto(Guid Id, string Customer, string Sku, int Quantity);

public sealed record ListOrdersQuery : IQuery<Result<IReadOnlyList<OrderDto>>>;

public sealed class ListOrdersHandler(IOrderRepository repository)
    : IQueryHandler<ListOrdersQuery, Result<IReadOnlyList<OrderDto>>>
{
    public async Task<Result<IReadOnlyList<OrderDto>>> HandleAsync(ListOrdersQuery query, CancellationToken ct)
    {
        var orders = await repository.GetListAsync(specification: null, ct);
        IReadOnlyList<OrderDto> dtos = orders.Select(o => new OrderDto(o.Id, o.Customer, o.Sku, o.Quantity)).ToList();
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
}
