using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.Dapper;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using Slice.Mediator.Default;
using Slice.Modularity;
using Slice.Sample.Monolith.Contracts;

namespace Slice.Sample.Monolith.Inventory;

/// <summary>The Inventory bounded context — owns <c>inventory.db</c> and uses <b>Dapper</b>; reacts to
/// <c>OrderPlacedEto</c> and publishes <c>StockReservedEto</c>.</summary>
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SliceEntityFrameworkCoreModule), typeof(SliceDapperModule))]
public sealed class InventoryModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(InventoryModule).Assembly;

        services.AddSliceMediator();
        services.AddRequestHandlers(assembly);
        services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);
        services.AddDistributedEventHandlers(assembly);   // ReserveStockOnOrderPlaced
        services.AddSliceConventions(assembly);

        services.AddSliceDbContext<InventoryDbContext>(o => o.UseSqlite("Data Source=inventory.db"));
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Seed some stock to reserve against.
        if (!await db.Stock.AnyAsync())
        {
            db.Stock.Add(new StockItem { Sku = "BOOK-1", Available = 100, Reserved = 0 });
            db.Stock.Add(new StockItem { Sku = "PEN-2", Available = 500, Reserved = 0 });
            await db.SaveChangesAsync();
        }
    }
}
