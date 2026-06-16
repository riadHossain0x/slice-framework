using Slice.AspNetCore;
using Slice.Modularity;
using Slice.Sample.Monolith.Billing;
using Slice.Sample.Monolith.Inventory;
using Slice.Sample.Monolith.Notifications;
using Slice.Sample.Monolith.Sales;

namespace Slice.Sample.Monolith.Host;

/// <summary>
/// The composition root of the modular monolith: four bounded-context modules — each with its own
/// database and data-access stack — wired into one process. They communicate only through integration
/// events on the in-process distributed event bus.
/// </summary>
[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(SalesModule),          // EF       · sales.db
    typeof(BillingModule),        // LinqToDB · billing.db
    typeof(InventoryModule),      // Dapper   · inventory.db
    typeof(NotificationsModule))] // no DB
public sealed class HostModule : SliceModule;
