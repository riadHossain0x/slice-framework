using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using Slice.Mediator.Default;
using Slice.Modularity;
using Slice.Sample.Monolith.Contracts;

namespace Slice.Sample.Monolith.Sales;

/// <summary>The Sales bounded context — owns <c>sales.db</c> (EF) and publishes <c>OrderPlacedEto</c>.</summary>
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SliceEntityFrameworkCoreModule))]
public sealed class SalesModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(SalesModule).Assembly;

        services.AddSliceMediator();
        services.AddRequestHandlers(assembly);
        services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);   // shared event-name registry
        services.AddValidatorsFromAssembly(assembly);
        services.AddSliceConventions(assembly);

        services.AddSliceDbContext<SalesDbContext>(o => o.UseSqlite("Data Source=sales.db"));
        services.AddScoped<IOrderRepository, EfOrderRepository>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SalesDbContext>().Database.EnsureCreatedAsync();
    }
}
