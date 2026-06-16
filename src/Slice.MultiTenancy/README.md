# Slice.MultiTenancy

> Ambient current-tenant accessor and a claim/header tenant-resolution pipeline (middleware + mediator behavior) for Slice.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This project provides the request-side of multi-tenancy. `CurrentTenant` is an `AsyncLocal`-backed `ICurrentTenant` whose `Change(...)` pushes a tenant for the current async flow and restores the previous value on dispose. A resolution pipeline (`ITenantResolver` running ordered `ITenantResolveContributor`s) discovers the tenant from the `tenant_id` claim or the `X-Tenant-Id` header. `MultiTenancyMiddleware` applies it per HTTP request, while `MultiTenancyBehavior` covers non-HTTP entry points (e.g. background jobs). Once a tenant is ambient, EF Core's query filter enforces row-level isolation; for database-per-tenant isolation see `Slice.EntityFrameworkCore`'s `AddSliceMultiTenantDbContext`.

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Mediator`, `Slice.Modularity`, `Slice.Application`
- **Third-party:** `Microsoft.AspNetCore.App` (framework reference), `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

`SliceMultiTenancyModule` `[DependsOn(typeof(SliceApplicationModule))]` registers `IHttpContextAccessor`, the conventional services in the assembly (`CurrentTenant` replacing the Core null default, `TenantResolver`, and the claim/header contributors), and the open-generic `MultiTenancyBehavior<,>` as an `IPipelineBehavior<,>`. Web hosts also call `app.UseSliceMultiTenancy()`.

```csharp
[DependsOn(typeof(SliceMultiTenancyModule))]
public sealed class ApiModule : SliceModule;

// in the pipeline (after authentication, before endpoints)
app.UseSliceMultiTenancy();
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `CurrentTenant` | sealed class | `AsyncLocal`-backed `ICurrentTenant` (`ISingletonDependency`); `Change` pushes/restores the ambient tenant. |
| `TenantInfo` | sealed record | Immutable ambient snapshot (`Id`, `Name`). |
| `ITenantResolveContributor` | interface | One tenant-discovery strategy; mutates a `TenantResolveResult`. |
| `ITenantResolver` | interface | Runs contributors in order; first to resolve wins. |
| `TenantResolver` | sealed class | Default resolver (`ITransientDependency`) over the registered contributors. |
| `TenantResolveResult` | sealed class | Carries the resolved `TenantId`; `Resolved` is `true` once set. |
| `ClaimTenantResolveContributor` | sealed class | Resolves from the `tenant_id` claim (`ITransientDependency`). |
| `HeaderTenantResolveContributor` | sealed class | Resolves from the `X-Tenant-Id` request header (`ITransientDependency`). |
| `TenantConstants` | static class | `Header = "X-Tenant-Id"`, `Claim = "tenant_id"`. |
| `MultiTenancyMiddleware` | sealed class | Resolves the tenant per request and pushes it onto `ICurrentTenant`. |
| `MultiTenancyMiddlewareExtensions` | static class | `UseSliceMultiTenancy()` extension. |
| `MultiTenancyBehavior<TRequest, TResponse>` | sealed class | Mediator pipeline behavior (order `PipelineOrder.MultiTenancy`) that resolves a tenant when none is ambient. |
| `SliceMultiTenancyModule` | sealed class | The multi-tenancy module. |

## Usage

### Reading and changing the ambient tenant

```csharp
public sealed class ReportHandler(ICurrentTenant currentTenant) /* ... */
{
    public async Task RunAsync()
    {
        if (currentTenant.IsAvailable)
            Console.WriteLine($"tenant {currentTenant.Id} ({currentTenant.Name})");

        // run a block under a specific tenant; the previous value is restored on dispose
        using (currentTenant.Change(someTenantId, "Acme"))
        {
            // queries here are scoped to someTenantId (EF query filter applies)
        }
    }
}
```

### Resolution

`MultiTenancyMiddleware` runs `ITenantResolver.ResolveAsync` for each request and wraps the rest of the pipeline in `currentTenant.Change(result.TenantId)`. For requests that arrive without HTTP middleware, `MultiTenancyBehavior` does the same around the mediator handler — but only when no tenant is already ambient and only if resolution succeeds.

Adding a custom contributor (e.g. subdomain) means implementing `ITenantResolveContributor`; contributors run in registration order and the first to set `TenantId` wins.

## Notes

- `CurrentTenant` is registered as a singleton via convention and replaces the Core null default, so it wins resolution; the actual per-flow value lives in an `AsyncLocal`.
- `TenantResolver` and both contributors are transient; the behavior is registered as transient open-generic.
- The middleware always wraps the pipeline in a `Change(...)` (even when the result is unresolved, pushing a `null` tenant); the behavior is a no-op when a tenant is already available and only changes tenant when resolution produced one.
- **Row-level isolation** (the default) relies on the EF Core multi-tenant query filter keyed off `ICurrentTenant.Id`. For **database-per-tenant** isolation, combine this with `Slice.EntityFrameworkCore`'s `AddSliceMultiTenantDbContext` + `AddTenantConnectionStrings`.
