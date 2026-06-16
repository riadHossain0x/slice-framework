# Modularity & dependency injection

Slice composes an application from **modules**. A module is a unit of capability that declares its
dependencies and registers its services. The host names one *root* module; the framework discovers
the rest by walking `[DependsOn]`.

Packages: `Slice.Modularity` (the loader + conventions), `Slice.Core` (the DI marker interfaces).

> Runnable example: [`samples/modular-monolith`](../samples/modular-monolith/) — four bounded-context
> modules in one host, each on its own database + data-access stack (EF, LinqToDB, Dapper),
> communicating through in-process integration events.

---

## Defining a module

```csharp
using Slice.Modularity;

[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(SliceEntityFrameworkCoreModule),
    typeof(SliceAuthorizationModule))]
public sealed class CrmModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(CrmModule).Assembly;

        services.AddSliceMediator();                   // pick the mediator engine
        services.AddRequestHandlers(assembly);         // discover command/query handlers
        services.AddValidatorsFromAssembly(assembly);  // discover FluentValidation validators
        services.AddSliceConventions(assembly);        // register marker-interface services in THIS assembly

        services.AddSliceDbContext<CrmDbContext>(o => o.UseSqlite(/* … */));
        services.AddScoped<ILeadRepository, EfLeadRepository>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CrmDbContext>().Database.EnsureCreatedAsync();
    }
}
```

### `SliceModule` lifecycle

`SliceModule` is the base class. Override the hooks you need:

| Hook | When | Typical use |
|---|---|---|
| `PreConfigureServices(context)` | before any `ConfigureServices` | set options other modules read |
| `ConfigureServices(context)` | register services | the bulk of wiring |
| `PostConfigureServices(context)` | after all `ConfigureServices` | finalise/override registrations |
| `OnApplicationInitializationAsync(context)` | after the container is built | migrations, seeding, warm-up |
| `OnApplicationShutdownAsync(context)` | on shutdown | flush/cleanup |

`ServiceConfigurationContext` exposes `Services` (`IServiceCollection`), `Configuration`
(`IConfiguration`), and an `Items` dictionary for cross-module handshakes during configuration.
`ApplicationInitializationContext` exposes the built `ServiceProvider`.

### `[DependsOn]`

```csharp
[DependsOn(typeof(SliceAuthorizationModule), typeof(SliceSettingsModule))]
public sealed class MyModule : SliceModule;
```

Depending on a module guarantees it is configured **before** yours, so its services (and any defaults
you intend to override) already exist. You only need to list *direct* dependencies — transitive ones
are pulled in automatically.

---

## Composing & running the application

```csharp
var builder = WebApplication.CreateBuilder(args);

// Discover the whole module graph from one root, sort it, and configure each module once.
builder.Services.AddSliceModules<AppModule>(builder.Configuration);

var app = builder.Build();

// Run OnApplicationInitializationAsync for every module, in dependency order.
await app.Services.InitializeSliceModulesAsync();

// … middleware …
app.Run();
```

### How the loader works

1. Start at the root module; follow `[DependsOn]` edges to discover every reachable module.
2. **Topologically sort** with Kahn's algorithm. Ties are broken deterministically by `Type.FullName`,
   so the configuration order is stable across runs.
3. A cycle throws `SliceModuleCycleException` naming the involved modules.
4. Call `PreConfigureServices` → `ConfigureServices` → `PostConfigureServices` across all modules in
   that order (each phase fully completes before the next begins).
5. `InitializeSliceModulesAsync()` later runs `OnApplicationInitializationAsync` in the same order;
   `ShutdownSliceModulesAsync()` runs shutdown in reverse.

Because configuration happens **dependency-first**, a module that depends on another can override its
registrations (last writer wins) — the basis of the seam/adapter pattern.

---

## Convention-based registration

You rarely write `services.AddScoped<IFoo, Foo>()`. Instead, mark the implementation with a lifetime
marker and call `AddSliceConventions(assembly)` once per module.

```csharp
using Slice.Core.DependencyInjection;

public sealed class LeadPricingService : ILeadPricingService, IScopedDependency { /* … */ }
```

| Marker | Lifetime |
|---|---|
| `ITransientDependency` | Transient |
| `IScopedDependency` | Scoped |
| `ISingletonDependency` | Singleton |

`AddSliceConventions(assembly)` scans the assembly and, for each non-abstract class carrying a marker:

- registers the class as **self** (so it can be resolved by concrete type), and
- registers it against **each interface it implements** (except the marker interfaces themselves), and
- registers it against any **abstract base class that carries a marker** (this is how
  `PermissionDefinitionProvider`, `SettingDefinitionProvider`, and `FeatureDefinitionProvider`
  subclasses are discovered as `IEnumerable<TBase>`).

So `LeadPricingService` above becomes resolvable as `ILeadPricingService` *and* as
`LeadPricingService`, both scoped — with no explicit registration.

> Each module scans **its own** assembly. Framework modules call `AddSliceConventions` on their
> assembly; your module calls it on yours. This keeps registration local and predictable.

---

## Patterns & gotchas

- **Override a default:** in a module that `[DependsOn]` the one providing the default, call
  `services.RemoveAll<TSeam>()` then add your implementation. Because you are configured later, yours
  wins. (Most adapter packages expose an `AddSlice…` extension that does this for you.)
- **Lifetimes must be consistent across a seam.** If a default is `IScopedDependency`, its replacement
  should also be scoped (don't capture a scoped service in a singleton). The framework follows this —
  e.g. the local distributed-event publisher is scoped because it consumes the scoped bus.
- **Initialization vs. configuration.** Don't resolve services in `ConfigureServices` (the container
  isn't built). Do migrations/seeding in `OnApplicationInitializationAsync`, creating a scope.
- **Discovery helpers are per-assembly:** `AddRequestHandlers`, `AddValidatorsFromAssembly`,
  `AddDomainEventHandlers`, `AddDistributedEventHandlers`, `AddBackgroundJobHandlers`,
  `AddDistributedEvents`, and `AddSliceConventions` all take the assembly to scan.
