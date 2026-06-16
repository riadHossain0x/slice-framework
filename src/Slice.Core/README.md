# Slice.Core

> Foundation primitives for the Slice framework: ambient context abstractions, DI marker interfaces, and the functional `Result`/`Error` model.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.Core` is the bottom-most package in the dependency graph. It defines the cross-cutting abstractions every other layer leans on: ambient accessors (`IClock`, `ICurrentUser`, `ICurrentTenant`, `IGuidGenerator`, `IDistributedLock`, `IDataFilter`) together with their safe null/default implementations, the three dependency-injection marker interfaces that drive convention-based registration, and the `Result`/`Result<T>`/`Error` types used to represent expected success/failure outcomes without throwing. It has no dependencies beyond the BCL and is referenced by `Slice.Domain`, `Slice.Modularity`, and the rest of the stack.

## Dependencies

- **Slice:** none
- **Third-party:** none beyond the BCL

## Module & registration

`Slice.Core` defines no `SliceModule` of its own. Its concrete default implementations all carry the `ISingletonDependency` marker, so they are picked up automatically when an assembly that references them is scanned by `AddSliceConventions(assembly)` (from `Slice.Modularity`). The marker → lifetime mapping itself lives here:

```csharp
public interface ITransientDependency;  // → ServiceLifetime.Transient
public interface IScopedDependency;     // → ServiceLifetime.Scoped
public interface ISingletonDependency;  // → ServiceLifetime.Singleton
```

Implement one of these on a class and the conventional registrar (in `Slice.Modularity`) registers it against itself and its interfaces.

## Key types

| Type | Kind | Description |
|---|---|---|
| `ICurrentTenant` | interface | Ambient current tenant: `IsAvailable`, `Id`, `Name`, and `Change(Guid? tenantId, string? name = null)` to push a scope. |
| `ICurrentUser` | interface | Ambient authenticated user: `IsAuthenticated`, `Id`, `UserName`, `Roles`. |
| `IClock` | interface | Testable clock; `Now` always returns UTC. |
| `IGuidGenerator` | interface | `Create()` produces stable, index-friendly identifiers. |
| `Clock` | class | Default `IClock` (`ISingletonDependency`); `Now => DateTime.UtcNow`. |
| `SequentialGuidGenerator` | class | Default `IGuidGenerator` (`ISingletonDependency`); `Create() => Guid.CreateVersion7()`. |
| `IDataFilter` | interface | Ambient toggle for global query filters: `IsEnabled<TFilter>()`, `Disable<TFilter>()`, `Enable<TFilter>()`. |
| `DataFilter` | class | Default `IDataFilter` (`ISingletonDependency`) backed by `AsyncLocal`; filters default to enabled. |
| `IDistributedLock` | interface | Best-effort distributed mutex: `TryAcquireAsync(string key, TimeSpan? timeout = null, CancellationToken ct = default)`. |
| `NullDistributedLock` | class | Default `IDistributedLock` (`ISingletonDependency`); always "acquires" (single-node no-op). |
| `NullCurrentTenant` | class | Default no-tenant `ICurrentTenant` (`ISingletonDependency`). |
| `NullCurrentUser` | class | Default anonymous `ICurrentUser` (`ISingletonDependency`); `Roles => []`. |
| `ITransientDependency` | interface | DI marker → transient registration. |
| `IScopedDependency` | interface | DI marker → scoped registration. |
| `ISingletonDependency` | interface | DI marker → singleton registration. |
| `ErrorType` | enum | Classifies an `Error`: `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unauthorized`, `Unexpected`. |
| `Error` | record | Expected, non-exceptional failure: `Code`, `Message`, `Type`, optional `Details`. Factory methods per `ErrorType`. |
| `IResult` | interface | Non-generic view over a result: `IsSuccess`, `Error?`, `object? GetValue()`. |
| `Result` | readonly struct | Success/failure outcome with no value. |
| `Result<T>` | readonly struct | Success/failure outcome carrying a `Value` on success. |

## Usage

Producing and consuming results:

```csharp
using Slice.Core.Results;

// Non-generic outcome
Result Activate(User user)
{
    if (user.IsLocked)
        return Error.Conflict("User:Locked", "The user account is locked.");

    user.Activate();
    return Result.Success();
}

// Value-carrying outcome with implicit conversions
Result<User> Find(Guid id)
{
    var user = _store.Find(id);
    if (user is null)
        return Error.NotFound("User:NotFound", $"User '{id}' was not found."); // implicit Error → Result<T>
    return user; // implicit T → Result<T>.Success(value)
}

// Validation error with per-field details
var error = Error.Validation(
    "User:Invalid",
    "Validation failed.",
    new Dictionary<string, string[]> { ["Email"] = ["Email is required."] });

// Branching with Match
string message = Find(id).Match(
    onSuccess: u => $"Found {u.UserName}",
    onFailure: e => $"{e.Type}: {e.Message}");
```

Using ambient abstractions in a service:

```csharp
using Slice.Core.Ambient;
using Slice.Core.DependencyInjection;

public sealed class OrderFactory(IClock clock, IGuidGenerator guids, ICurrentUser user)
    : ITransientDependency
{
    public Order Create()
        => new(guids.Create(), createdBy: user.Id, createdAt: clock.Now);
}

// Temporarily bypass a global filter (e.g. soft-delete) within an async scope
using (dataFilter.Disable<ISoftDelete>())
{
    // queries inside here ignore the soft-delete filter
}

// Push a tenant scope; dispose restores the previous tenant
using (currentTenant.Change(tenantId, "Acme"))
{
    // ambient ICurrentTenant.Id == tenantId here
}
```

## Notes

- **Lifetimes:** all default implementations (`Clock`, `SequentialGuidGenerator`, `DataFilter`, `NullDistributedLock`, `NullCurrentTenant`, `NullCurrentUser`) are singletons via `ISingletonDependency`.
- **Null-object defaults:** `NullCurrentTenant`/`NullCurrentUser` are inert stand-ins, replaced by the MultiTenancy / auth modules when present. `NullCurrentTenant.Change(...)` returns a no-op `IDisposable`. `NullDistributedLock.TryAcquireAsync` always returns a disposable handle (never `null`), so single-node code "succeeds" by default.
- **AsyncLocal behavior:** `DataFilter` stores per-filter enabled state in `AsyncLocal<bool?>` slots keyed by filter type; `IsEnabled<TFilter>()` defaults to `true` when unset. `Disable`/`Enable` return an `IDisposable` that restores the previous value on dispose, so toggles are flow-scoped and nest correctly.
- **GUIDs:** `SequentialGuidGenerator` emits UUID v7 (`Guid.CreateVersion7()`) — time-ordered and index-friendly, unlike random v4.
- **Result semantics:** `Result`/`Result<T>` are `readonly struct`s. Business outcomes travel as `Error`; programming/infrastructure faults should throw. `Result<T>.Failure` stores `default` for the value. Implicit conversions exist from `Error` (→ failure) and, for `Result<T>`, from `T` (→ success). `IResult.GetValue()` returns the boxed value (`null` for the non-generic `Result`).
- **HTTP mapping:** `ErrorType` is the hook the web layer uses to translate an `Error` into a status code; `Error.Details` carries per-field validation messages.
