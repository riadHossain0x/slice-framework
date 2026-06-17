using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using Slice.Mediator.Default;
using Slice.Modularity;
using MonolithApp.Contracts;

namespace MonolithApp.Orders;

/// <summary>The Orders bounded context — owns <c>orders.db</c> (EF) and publishes <c>OrderPlacedEto</c>.</summary>
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SliceEntityFrameworkCoreModule))]
public sealed class OrdersModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(OrdersModule).Assembly;

        services.AddSliceMediator();
        services.AddRequestHandlers(assembly);
        services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);   // shared event-name registry
        services.AddValidatorsFromAssembly(assembly);
        services.AddSliceConventions(assembly);

        services.AddSliceDbContext<OrdersDbContext>(o => o.UseSqlite("Data Source=orders.db"));
        services.AddScoped<IOrderRepository, EfOrderRepository>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreatedAsync();
    }
}
