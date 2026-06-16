# Slice.AspNetCore.SignalR

> SignalR real-time hubs for Slice apps, with base hub classes that surface the connected caller's user and tenant from the connection principal.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.AspNetCore.SignalR` adds ASP.NET Core SignalR to a Slice application and provides `SliceHub` / `SliceHub<TClient>` base classes. These read the same `sub`/`tenant_id` claims the rest of Slice uses from the connection's `ClaimsPrincipal`, so real-time handlers are user- and tenant-aware without re-parsing claims by hand. A `SliceSignalRModule` wires SignalR into the module graph, and thin extensions register SignalR and map hubs.

## Dependencies

- **Slice:** `Slice.AspNetCore` (module depends on `SliceAspNetCoreModule`), `Slice.Modularity`
- **Third-party:** `FrameworkReference Microsoft.AspNetCore.App` (ASP.NET Core SignalR)

## Module & registration

Add `SliceSignalRModule` to your module graph; it depends on `SliceAspNetCoreModule` and calls `AddSignalR()`.

```csharp
[DependsOn(typeof(SliceSignalRModule))]
public sealed class MyRealtimeModule : SliceModule { }
```

Map hubs on the endpoint route builder:

```csharp
app.MapSliceHub<NotificationsHub>("/hubs/notifications");
```

If you are not composing via the module, register SignalR explicitly:

```csharp
builder.Services.AddSliceSignalR();
// or with options:
builder.Services.AddSliceSignalR(options => options.EnableDetailedErrors = true);
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `SliceHub` | Abstract class (`: Hub`) | Base hub exposing `CurrentPrincipal` (`Context.User`), `CurrentUserId` (from `sub` / `NameIdentifier`), `CurrentTenantId` (from `tenant_id`), and `CurrentUserName` (`Context.User?.Identity?.Name`), all `protected`. |
| `SliceHub<TClient>` | Abstract class (`: Hub<TClient>` where `TClient : class`) | Strongly-typed-client variant with the same identity helpers. |
| `SliceSignalRModule` | `SliceModule` (`[DependsOn(typeof(SliceAspNetCoreModule))]`) | Calls `AddSignalR()`. |
| `SliceSignalRExtensions.AddSliceSignalR(this IServiceCollection, Action<HubOptions>? configure = null)` | Extension method | Calls `AddSignalR()` (or `AddSignalR(configure)`); returns `ISignalRServerBuilder`. |
| `SliceSignalRExtensions.MapSliceHub<THub>(this IEndpointRouteBuilder, string pattern)` where `THub : Hub` | Extension method | Thin wrapper over `MapHub<THub>(pattern)`; returns `HubEndpointConventionBuilder`. |

## Usage

Define a hub that derives from `SliceHub` and uses the identity helpers:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Slice.AspNetCore.SignalR;

[Authorize]
public sealed class NotificationsHub : SliceHub
{
    public async Task Ping(string message)
    {
        // CurrentUserId / CurrentTenantId come from the connection principal's claims.
        await Clients.Caller.SendAsync(
            "Pong",
            new { UserId = CurrentUserId, TenantId = CurrentTenantId, message });
    }
}
```

Map it:

```csharp
app.MapSliceHub<NotificationsHub>("/hubs/notifications");
```

Invoke it from a TypeScript client:

```ts
const conn = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/notifications", { accessTokenFactory: () => token })
    .build();

conn.on("Pong", payload => console.log(payload));
await conn.start();
await conn.invoke("Ping", "hello");
```

A strongly-typed hub uses `SliceHub<TClient>`:

```csharp
public interface INotificationsClient
{
    Task Pong(object payload);
}

public sealed class NotificationsHub : SliceHub<INotificationsClient>
{
    public Task Ping(string message)
        => Clients.Caller.Pong(new { UserId = CurrentUserId, message });
}
```

## Notes

- `CurrentUserId` is read from the `sub` claim, falling back to `ClaimTypes.NameIdentifier`; both `CurrentUserId` and `CurrentTenantId` return `null` when the claim is missing or not a valid `Guid`.
- `CurrentTenantId` is read only from the `tenant_id` claim.
- The identity helpers depend on an authenticated connection — apply `[Authorize]` (and ensure SignalR can read the access token) so `Context.User` is populated.
- `MapSliceHub<THub>` and `AddSliceSignalR` are intentionally thin wrappers over the framework's `MapHub`/`AddSignalR`; they exist for naming symmetry with the rest of Slice. The `THub` constraint is `Hub` (not specifically `SliceHub`), so any SignalR hub can be mapped.
