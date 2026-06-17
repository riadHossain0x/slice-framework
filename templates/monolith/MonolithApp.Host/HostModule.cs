using Slice.AspNetCore;
using Slice.Modularity;
using MonolithApp.Billing;
using MonolithApp.Orders;

namespace MonolithApp.Host;

/// <summary>
/// The composition root of the modular monolith: two bounded-context modules — each owning its own
/// database — wired into one process. They communicate only through integration events on the
/// in-process distributed event bus (Orders publishes <c>OrderPlacedEto</c>; Billing reacts and
/// publishes <c>InvoiceCreatedEto</c>). Add more modules to <c>[DependsOn]</c> as the app grows.
/// </summary>
[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(OrdersModule),     // EF · orders.db   · publishes OrderPlacedEto
    typeof(BillingModule))]   // EF · billing.db  · reacts → InvoiceCreatedEto
public sealed class HostModule : SliceModule;
