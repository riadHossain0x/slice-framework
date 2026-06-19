using LinqToDB;
using LinqToDB.DataProvider.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.EntityFrameworkCore;
using Slice.EventBus;
using Slice.Features;
using Slice.LinqToDB;
using Slice.Mediator.Default;
using Slice.Modularity;
using Slice.Sample.Monolith.Contracts;

namespace Slice.Sample.Monolith.Billing;

/// <summary>The Billing bounded context — owns <c>billing.db</c> and uses <b>LinqToDB</b>; reacts to
/// <c>OrderPlacedEto</c> and publishes <c>InvoiceCreatedEto</c>. The whole module is gated behind the
/// <c>Billing</c> feature (one line below); the event handler guards itself separately (see Billing.cs).</summary>
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SliceEntityFrameworkCoreModule), typeof(SliceLinqToDbModule), typeof(SliceFeaturesModule))]
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

        // Gate every Billing mediator request (e.g. ListInvoicesQuery → GET /api/invoices) behind the
        // "Billing" feature — one line instead of [RequiresFeature] on each slice.
        services.RequireFeature<BillingModule>(BillingFeatures.Billing);

        services.AddSliceDbContext<BillingDbContext>(o => o.UseSqlite("Data Source=billing.db"));
        services.AddSliceLinqToDb<BillingDbContext>(SQLiteTools.GetDataProvider(ProviderName.SQLiteMS));
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.EnsureCreatedAsync();
    }
}
