using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.Application;
using Slice.AspNetCore.Mvc;
using Slice.Core.Ambient;
using Slice.Core.Results;
using Slice.Domain.Entities;
using Slice.Domain.MultiTenancy;
using Slice.Domain.Repositories;
using Slice.EntityFrameworkCore;
using Slice.Management;

namespace Slice.Sample.MultiTenant;

/// <summary>The two demo tenants — pass one as the <c>X-Tenant-Id</c> header.</summary>
public static class DemoTenants
{
    public static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

// ── Domain ──
public sealed class Widget : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private Widget() { }

    public Widget(Guid id, Guid? tenantId, string name) : base(id)
    {
        TenantId = tenantId;
        Name = name;
    }

    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
}

public interface IWidgetRepository : IRepository<Widget, Guid>;

// ── Persistence — the connection string is resolved per request from the current tenant ──
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options, ICurrentTenant t, IDataFilter f)
    : SliceDbContext(options, t, f)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Widget>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.Property(x => x.Name).IsRequired();
        });
        base.OnModelCreating(b);
    }
}

public sealed class EfWidgetRepository(TenantDbContext db) : EfRepository<TenantDbContext, Widget, Guid>(db), IWidgetRepository;

// ── Feature: create a widget (lands in the current tenant's database) ──
public sealed record CreateWidgetCommand(string Name) : ICommand<Result<Guid>>;

public sealed class CreateWidgetValidator : AbstractValidator<CreateWidgetCommand>
{
    public CreateWidgetValidator() => RuleFor(x => x.Name).NotEmpty();
}

public sealed class CreateWidgetHandler(IWidgetRepository repository, IGuidGenerator guids, ICurrentTenant tenant)
    : ICommandHandler<CreateWidgetCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateWidgetCommand command, CancellationToken ct)
    {
        var widget = new Widget(guids.Create(), tenant.Id, command.Name);
        await repository.InsertAsync(widget, autoSave: false, ct);
        return Result<Guid>.Success(widget.Id);
    }
}

// ── Feature: list the current tenant's widgets ──
public sealed record WidgetDto(Guid Id, string Name, Guid? TenantId);

public sealed record ListWidgetsQuery : IQuery<Result<IReadOnlyList<WidgetDto>>>;

public sealed class ListWidgetsHandler(IWidgetRepository repository)
    : IQueryHandler<ListWidgetsQuery, Result<IReadOnlyList<WidgetDto>>>
{
    public async Task<Result<IReadOnlyList<WidgetDto>>> HandleAsync(ListWidgetsQuery query, CancellationToken ct)
    {
        var widgets = await repository.GetListAsync(specification: null, ct);
        IReadOnlyList<WidgetDto> dtos = widgets.Select(w => new WidgetDto(w.Id, w.Name, w.TenantId)).ToList();
        return Result<IReadOnlyList<WidgetDto>>.Success(dtos);
    }
}

[Route("api/widgets")]
public sealed class WidgetsController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateWidgetCommand command, CancellationToken ct) => SendAsync(command, ct);

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => SendAsync(new ListWidgetsQuery(), ct);
}

// ── Runtime tenant onboarding ───────────────────────────────────────────────
// Registers a new tenant in the registry with its own connection string and provisions its database
// on the fly — no code change, no redeploy. (In production the connection string would be issued by
// your infrastructure / secret store rather than derived from the id.)
public sealed record OnboardTenantCommand(string Name) : ICommand<Result<OnboardedTenant>>;
public sealed record OnboardedTenant(Guid Id, string ConnectionString);

public sealed class OnboardTenantValidator : AbstractValidator<OnboardTenantCommand>
{
    public OnboardTenantValidator() => RuleFor(x => x.Name).NotEmpty();
}

public sealed class OnboardTenantHandler(
    SliceManagementDbContext registry,
    ManagementTenantConnectionStore connectionStore,
    IGuidGenerator guids,
    ICurrentTenant currentTenant,
    IServiceScopeFactory scopeFactory) : ICommandHandler<OnboardTenantCommand, Result<OnboardedTenant>>
{
    public async Task<Result<OnboardedTenant>> HandleAsync(OnboardTenantCommand command, CancellationToken ct)
    {
        var id = guids.Create();
        var connectionString = $"Data Source=tenant-{id:N}.db";

        // 1) Register the tenant + its database in the registry (persist now so the store can read it).
        registry.Tenants.Add(new TenantRecord { Id = id, Name = command.Name, ConnectionString = connectionString });
        await registry.SaveChangesAsync(ct);
        connectionStore.Invalidate(id);   // ensure the cache reloads this new tenant

        // 2) Provision the tenant's dedicated database (resolve the context under the new tenant).
        using (currentTenant.Change(id))
        using (var scope = scopeFactory.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database.EnsureCreatedAsync(ct);

        return Result<OnboardedTenant>.Success(new OnboardedTenant(id, connectionString));
    }
}

[Route("api/tenants")]
public sealed class TenantsController : SliceController
{
    [HttpPost]
    public Task<IActionResult> Onboard([FromBody] OnboardTenantCommand command, CancellationToken ct) => SendAsync(command, ct);
}
