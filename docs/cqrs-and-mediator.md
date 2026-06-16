# CQRS & the mediator pipeline

Every use case in Slice is a **request** (a command or a query) handled by exactly one **handler**,
dispatched through a **mediator** whose **pipeline behaviors** apply cross-cutting concerns in a fixed
order. This is what keeps a vertical slice tiny: the slice expresses intent; the pipeline supplies the
machinery.

Packages: `Slice.Mediator` (abstraction), `Slice.Mediator.Default` (the engine), `Slice.Application`
(CQRS markers + the standard behaviors), `Slice.Mediator.MediatR` (optional engine swap).

---

## Requests & handlers

`Slice.Mediator` defines the primitives:

```csharp
public interface IRequest<out TResponse>;
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}
public interface ISender
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
```

`Slice.Application` layers CQRS intent on top — commands change state, queries read it:

```csharp
public interface ICommand<TResponse> : IRequest<TResponse>;   // also ICommand : IRequest<Result>
public interface IQuery<TResponse>   : IRequest<TResponse>;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
```

By convention the response is a [`Result<T>`](web-and-results.md#the-result-model) so expected failures
(validation, permission, not-found) flow as data rather than exceptions.

```csharp
public sealed record GetLeadQuery(Guid Id) : IQuery<Result<LeadDto>>;

public sealed class GetLeadHandler(ILeadRepository repository) : IQueryHandler<GetLeadQuery, Result<LeadDto>>
{
    public async Task<Result<LeadDto>> HandleAsync(GetLeadQuery query, CancellationToken ct)
    {
        var lead = await repository.FindAsync(query.Id, ct);
        return lead is null
            ? Result<LeadDto>.Failure(Error.NotFound("Lead.NotFound", "Lead not found."))
            : Result<LeadDto>.Success(LeadDto.From(lead));
    }
}
```

Handlers are discovered and registered (transient) by `AddRequestHandlers(assembly)` in your module.

---

## Pipeline behaviors

A behavior wraps the handler. It can run logic before/after, short-circuit, or transform the response:

```csharp
public interface IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}
```

### Deterministic ordering

Behaviors run in a **fixed order independent of registration**, because each implements
`IHasPipelineOrder` and returns one of the `PipelineOrder` constants (lower runs first / outermost):

| Order | Constant | Behavior (package) | Responsibility |
|------:|----------|--------------------|----------------|
| 100 | `PipelineOrder.Logging` | `LoggingBehavior` (`Slice.Application`) | log start / success / failure |
| 200 | `PipelineOrder.MultiTenancy` | `MultiTenancyBehavior` (`Slice.MultiTenancy`) | ensure the ambient tenant is established |
| 300 | `PipelineOrder.Authorization` | `AuthorizationBehavior` (`Slice.Authorization`) | enforce `[SlicePermission]` |
| 350 | `PipelineOrder.FeatureCheck` | `RequiresFeatureBehavior` (`Slice.Features`) | enforce `[RequiresFeature]` |
| 400 | `PipelineOrder.Validation` | `ValidationBehavior` (`Slice.Application`) | run FluentValidation |
| 500 | `PipelineOrder.UnitOfWork` | `UnitOfWorkBehavior` (`Slice.Application`) | commit after the handler succeeds |
| `int.MaxValue` | `PipelineOrder.Default` | (your behaviors, default) | innermost |

Because the order is data, you can add behaviors in any module without worrying about registration
sequence. To insert your own, implement `IPipelineBehavior<,>` + `IHasPipelineOrder` and pick a number
between the relevant stages.

### What each standard behavior does

- **`LoggingBehavior` (100)** — logs the request type on entry, and success or the failure
  `Result`/exception on exit.
- **`MultiTenancyBehavior` (200)** — makes sure the ambient `ICurrentTenant` is set for the request
  (the middleware sets it for HTTP; this covers non-HTTP entry points). See [Multi-tenancy](multitenancy.md).
- **`AuthorizationBehavior` (300)** — reads `[SlicePermission]` on the request type and calls
  `IPermissionChecker`. On denial it returns `Error.Forbidden(...)` as a failed `Result` and the
  handler never runs. See [Security](security.md).
- **`RequiresFeatureBehavior` (350)** — reads `[RequiresFeature]` and checks `IFeatureChecker`; a
  disabled feature short-circuits to `Error.Forbidden("Features:Disabled", …)`.
- **`ValidationBehavior` (400)** — resolves all `IValidator<TRequest>` (FluentValidation), aggregates
  failures into `Error.Validation("Validation", "One or more validation errors occurred.", details)`,
  and returns it without invoking the handler when invalid.
- **`UnitOfWorkBehavior` (500)** — after the handler returns a **successful** result, calls
  `SaveChangesAsync()` on every registered `IUnitOfWork` (your `SliceDbContext`s). If the handler
  returns a failed `IResult`, it skips the save (nothing is committed). This is why handlers insert
  with `autoSave: false`.

> **Short-circuiting:** any behavior that returns a failed `Result` stops the pipeline — later
> behaviors and the handler don't run, and the unit of work doesn't commit. Behaviors short-circuit by
> producing a value of the response type; `ResultFactory.FailureOrThrow<TResponse>(error)` builds the
> right failed `Result`/`Result<T>` (or throws `SlicePipelineException` if `TResponse` isn't a result
> type).

---

## Choosing the mediator engine

### Default engine

```csharp
services.AddSliceMediator();          // registers the built-in ISender (DefaultSender)
services.AddRequestHandlers(assembly); // discovers and registers handlers (transient)
```

The default engine builds the behavior chain from the registered `IPipelineBehavior<,>` services,
orders them by `IHasPipelineOrder`, and caches the composed pipeline per request type via a shared
`RequestPipeline` executor (generic and boxed paths).

### MediatR adapter

To run on MediatR instead (e.g. to reuse MediatR-specific tooling), swap the engine:

```csharp
services.AddSliceMediatorMediatR();   // RemoveAll<ISender>() + a MediatR-backed ISender
```

Requests still travel through Slice's `RequestPipeline` (via a non-generic `SliceRequestEnvelope`), so
your behaviors and ordering are preserved — only the dispatch engine changes. Your slices,
`ICommand`/`IQuery`, validators, and behaviors are untouched.

---

## Calling the mediator

From a controller (the common path), `SliceController.SendAsync` forwards to `ISender` and maps the
`Result<T>` to HTTP:

```csharp
[HttpPost]
public Task<IActionResult> Create([FromBody] CreateLeadCommand command, CancellationToken ct)
    => SendAsync(command, ct);
```

From anywhere else, inject `ISender`:

```csharp
public sealed class LeadImporter(ISender sender)
{
    public Task<Result<Guid>> ImportAsync(string first, string last, CancellationToken ct)
        => sender.SendAsync(new CreateLeadCommand(first, last, null), ct);
}
```
