# Slice.Application

> The CQRS + cross-cutting application layer: command/query contracts, the standard pipeline behaviors (logging, FluentValidation, unit-of-work), and the `Result`-aware failure factory.

Part of the **Slice** framework — a .NET 10 Vertical Slice Architecture + DDD application framework. See the [root README](../../README.md) and [docs](../../docs/) for the big picture.

## Overview

This library turns the engine-agnostic `Slice.Mediator` abstractions into an opinionated application layer. It splits requests into **commands** (state-changing, go through the unit-of-work) and **queries** (read-only, bypass it), supplies three ordered pipeline behaviors, and integrates **FluentValidation** so invalid requests short-circuit to a failed `Result` instead of reaching handlers. `SliceApplicationModule` wires these behaviors and the ambient core services; feature modules depend on it.

## Dependencies

- **Slice:** `Slice.Mediator`, `Slice.Domain`, `Slice.Modularity` (and `Slice.Core` for `Result`/`Error`, transitively)
- **Third-party:** `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`

## Module & registration

`SliceApplicationModule : SliceModule` registers the standard behaviors (registration order = outermost-first, but final chain position is driven by each behavior's `IHasPipelineOrder.Order`) and the ambient core services. Feature modules declare it as a dependency:

```csharp
using Slice.Application;
using Slice.Modularity;

[DependsOn(typeof(SliceApplicationModule))]
public sealed class MyFeatureModule : SliceModule { }
```

Inside `SliceApplicationModule.ConfigureServices`:

```csharp
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));
services.AddSliceConventions(typeof(Clock).Assembly); // ambient core services (Clock, GuidGenerator)
```

## Key types

| Type | Kind | Description |
|---|---|---|
| `ICommandBase` | interface | Non-generic marker so `UnitOfWorkBehavior` can detect commands. |
| `ICommand<TResponse>` | interface | State-changing request; `IRequest<TResponse>, ICommandBase`. |
| `ICommand` | interface | State-changing request with no return value; `IRequest<Result>, ICommandBase`. |
| `IQuery<TResponse>` | interface | Read-only request; `IRequest<TResponse>`. Bypasses the unit-of-work. |
| `ICommandHandler<TCommand, TResponse>` | interface | `IRequestHandler<TCommand, TResponse>` where `TCommand : ICommand<TResponse>`. |
| `IQueryHandler<TQuery, TResponse>` | interface | `IRequestHandler<TQuery, TResponse>` where `TQuery : IQuery<TResponse>`. |
| `LoggingBehavior<TRequest, TResponse>` | `sealed class` | Behavior, `Order => PipelineOrder.Logging` (100). Logs request name + elapsed ms; outermost. |
| `ValidationBehavior<TRequest, TResponse>` | `sealed class` | Behavior, `Order => PipelineOrder.Validation` (400). Runs FluentValidation validators. |
| `UnitOfWorkBehavior<TRequest, TResponse>` | `sealed class` | Behavior, `Order => PipelineOrder.UnitOfWork` (500). Saves all `IUnitOfWork`s after a successful command. |
| `IUnitOfWork` | interface | `Task<int> SaveChangesAsync(CancellationToken ct = default)`. Implemented per DbContext. |
| `ResultFactory` | `static class` | `TResponse FailureOrThrow<TResponse>(Error error)` — typed `Result`/`Result<T>` failure, else throws. |
| `SlicePipelineException` | `sealed class : Exception` | Thrown when a pipeline failure can't be expressed as a `Result`. Carries `Error Error { get; }`. |

## Pipeline behaviors

The chain runs by ascending `IHasPipelineOrder.Order` (outermost → innermost):

- **`LoggingBehavior` (100)** — logs `Handling {Request}` (Debug), `Handled {Request} in {Elapsed}ms` (Information) on success, and `Error handling {Request} after {Elapsed}ms` (Error) then rethrows on exception. Uses `Stopwatch.GetTimestamp()` / `GetElapsedTime`.
- **`ValidationBehavior` (400)** — resolves all `IValidator<TRequest>` registered for the request. If none, passes through. Otherwise runs them with `Task.WhenAll`, collects failures, and on any failure groups errors by `PropertyName` into `IReadOnlyDictionary<string, string[]>`, builds `Error.Validation("Validation", "One or more validation errors occurred.", details)`, and returns `ResultFactory.FailureOrThrow<TResponse>(error)`.
- **`UnitOfWorkBehavior` (500)** — passes queries straight through (anything not `ICommandBase`). For commands it runs the handler, then if the response is `IResult { IsSuccess: false }` it returns **without saving**; otherwise it `await`s `SaveChangesAsync(ct)` on every registered `IUnitOfWork`.

### Validation → `Result` integration

`ResultFactory.FailureOrThrow<TResponse>` (cached `ConcurrentDictionary<Type, …>`) returns:
- `Result.Failure(error)` when `TResponse` is `Result`,
- `Result<T>.Failure(error)` (via reflection on the static `Failure` method) when `TResponse` is `Result<T>`,
- otherwise throws `SlicePipelineException(error)`.

So validation failures short-circuit cleanly **only** when the command/query returns `Result` or `Result<T>`; non-result responses surface as an exception.

### Unit-of-work commit behavior

The behavior follows an **`autoSave: false`** pattern: handlers mutate aggregates and add domain events **without** saving; the single commit happens in `UnitOfWorkBehavior` after the command succeeds. A failed `Result` (or a thrown exception) leaves all changes unsaved — the failure short-circuits before the `SaveChangesAsync` loop.

## Usage — end-to-end vertical slice

A command record, its FluentValidation validator, and its handler:

```csharp
using FluentValidation;
using Slice.Application;
using Slice.Core.Results;

// Command — returns a Result<Guid> so validation can short-circuit to a typed failure.
public sealed record CreateCustomer(string Name, string Email) : ICommand<Result<Guid>>;

// Validator — discovered by ValidationBehavior via IValidator<CreateCustomer>.
public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomer>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

// Handler — mutates state without saving; UnitOfWorkBehavior commits on success.
public sealed class CreateCustomerHandler(ICustomerRepository repository)
    : ICommandHandler<CreateCustomer, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateCustomer request, CancellationToken ct)
    {
        var customer = Customer.Create(request.Name, request.Email);
        await repository.AddAsync(customer, ct); // no SaveChanges here
        return Result<Guid>.Success(customer.Id);
    }
}
```

A read-only query:

```csharp
public sealed record GetCustomer(Guid Id) : IQuery<Result<CustomerDto>>;

public sealed class GetCustomerHandler(ICustomerRepository repository)
    : IQueryHandler<GetCustomer, Result<CustomerDto>>
{
    public async Task<Result<CustomerDto>> HandleAsync(GetCustomer request, CancellationToken ct)
    {
        var customer = await repository.FindAsync(request.Id, ct);
        return customer is null
            ? Error.NotFound("Customer.NotFound", "Customer not found.")
            : new CustomerDto(customer.Id, customer.Name);
    }
}
```

## Notes

- **Lifetimes:** all three behaviors are registered as open generics **transient** (`IPipelineBehavior<,>`). Handlers/validators are registered elsewhere (`AddRequestHandlers` and FluentValidation's assembly scan).
- **Validators are optional:** `ValidationBehavior` passes through when no `IValidator<TRequest>` is registered.
- **Commit only on success:** `UnitOfWorkBehavior` keys off `ICommandBase` for commands and skips saving when the response is a failed `IResult`; queries never touch the unit-of-work.
- **Return `Result`/`Result<T>` from commands and queries** so validation failures (and other behavior failures) short-circuit without throwing — otherwise `ResultFactory` throws `SlicePipelineException`.
- `Result`/`Result<T>` carry implicit conversions: a bare value becomes `Success`, and an `Error` becomes `Failure` (as used in the query example above).
