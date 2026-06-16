# Slice.ObjectMapping

> A thin, convention-based object-mapping abstraction over hand-written or source-generated (Mapperly/AutoMapper) typed mappers.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

`Slice.ObjectMapping` decouples mapping call-sites from concrete mapper implementations. You write (or generate) one small class per source→destination pair that implements `IObjectMapper<TSource, TDestination>`, register it by convention, and resolve mappings anywhere through the single `IObjectMapper` service-locator. There is no reflection-heavy mapping engine here — the actual mapping is whatever your typed mapper does (by hand, Mapperly, AutoMapper, etc.).

## Dependencies

- **Slice:** `Slice.Core`, `Slice.Modularity`, `Slice.Application` (the module depends on `SliceApplicationModule`)
- **Third-party:** `Microsoft.Extensions.DependencyInjection.Abstractions`

## Module & registration

Add `SliceObjectMappingModule` to your application's module graph. It registers the resolving `ObjectMapper` (and any other convention-marked types in the assembly) via `AddSliceConventions`.

```csharp
[DependsOn(typeof(SliceObjectMappingModule))]
public sealed class MyFeatureModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Register your typed mappers (one per pair). They are picked up by
        // convention if marked with a Slice lifetime marker, e.g. ITransientDependency.
    }
}
```

`ObjectMapper` itself is registered as `IObjectMapper` and marked `ITransientDependency`.

## Key types

| Type | Kind | Description |
|---|---|---|
| `IObjectMapper<in TSource, out TDestination>` | Interface | A typed mapper for one pair; single method `TDestination Map(TSource source)`. Implement by hand or generate with Mapperly/AutoMapper. |
| `IObjectMapper` | Interface | Service-locator facade; `TDestination Map<TSource, TDestination>(TSource source)` resolves and invokes the registered typed mapper. |
| `ObjectMapper` | Class (`sealed`, `IObjectMapper`, `ITransientDependency`) | Default implementation; resolves `IObjectMapper<TSource, TDestination>` from the `IServiceProvider`, throwing `InvalidOperationException` if none is registered. |
| `SliceObjectMappingModule` | `SliceModule` (`[DependsOn(typeof(SliceApplicationModule))]`) | Registers `ObjectMapper` via `AddSliceConventions`. |

## Usage

Define a typed mapper for a pair (here, hand-written):

```csharp
public sealed class CustomerToDtoMapper : IObjectMapper<Customer, CustomerDto>, ITransientDependency
{
    public CustomerDto Map(Customer source) => new()
    {
        Id = source.Id,
        Name = source.Name,
    };
}
```

Resolve mappings through `IObjectMapper`:

```csharp
public sealed class CustomerQueryHandler(IObjectMapper mapper)
{
    public CustomerDto Handle(Customer customer)
        => mapper.Map<Customer, CustomerDto>(customer);
}
```

You can also inject the typed mapper directly when you only need one pair:

```csharp
public sealed class CustomerQueryHandler(IObjectMapper<Customer, CustomerDto> mapper)
{
    public CustomerDto Handle(Customer customer) => mapper.Map(customer);
}
```

## Notes

- `ObjectMapper` is registered **transient** (`ITransientDependency`).
- `IObjectMapper.Map<TSource, TDestination>` throws `InvalidOperationException` — `No IObjectMapper<TSource, TDestination> is registered.` — if the corresponding typed mapper was never registered. Ensure each pair you call has a matching `IObjectMapper<,>` registration.
- This library provides no mapping logic itself; correctness of a mapping lives entirely in your `IObjectMapper<,>` implementation.
- Mappers are resolved per-call from `IServiceProvider`, so they participate in normal DI scoping/lifetimes.
