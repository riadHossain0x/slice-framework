using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using Slice.Mediator.Default;
using Slice.Modularity;
using MonolithApp.Contracts;

namespace MonolithApp.Billing;

/// <summary>The Billing bounded context — owns <c>billing.db</c> (EF); reacts to <c>OrderPlacedEto</c>
/// and publishes <c>InvoiceCreatedEto</c>.</summary>
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SliceEntityFrameworkCoreModule))]
public sealed class BillingModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(BillingModule).Assembly;

        services.AddSliceMediator();
        services.AddRequestHandlers(assembly);
        services.AddDistributedEvents(typeof(OrderPlacedEto).Assembly);
        services.AddDistributedEventHandlers(assembly);   // CreateInvoiceOnOrderPlaced
        services.AddSliceConventions(assembly);

        services.AddSliceDbContext<BillingDbContext>(o => o.UseSqlite("Data Source=billing.db"));
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.EnsureCreatedAsync();
    }
}
