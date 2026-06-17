# Microservices with Slice

Slice is **modular-monolith-first**, and that is exactly what makes it a pragmatic path to
microservices: a bounded-context **module** is the unit you later extract into a service, and because
cross-module communication already flows through the `IDistributedEventBus` seam — **local loopback by
default, a broker transport when you swap it** — the *same module code* runs in-process (one host) or
out-of-process (many hosts) without rewrites.

> **Recommendation:** start as a [modular monolith](../samples/modular-monolith/), extract a module into
> its own service only when team size, independent scaling, or independent deployment actually demands
> it. You get microservice-ready seams from day one without paying the distributed-systems tax up front.

This guide maps every microservice concern to one of three buckets: **Slice provides it**, **you bring a
platform component**, or **it's a current framework gap** (collected as an [issue-ready backlog](#issue-ready-backlog)
at the end). It builds on [Architecture](architecture.md), [Modularity & DI](modularity-and-di.md),
[Messaging & events](messaging.md), [Security](security.md) and [Multi-tenancy](multitenancy.md).

---

## 1. The extraction model: monolith → services

In the [modular-monolith sample](../samples/modular-monolith/), one `Host` composes four modules
(`Sales`, `Billing`, `Inventory`, `Notifications`) that talk only through **integration events** defined
in a shared **`Contracts`** project. The composition root is just a `[DependsOn(...)]` list:

```csharp
[DependsOn(typeof(SliceAspNetCoreModule), typeof(SalesModule), typeof(BillingModule),
           typeof(InventoryModule), typeof(NotificationsModule))]
public sealed class HostModule : SliceModule;
```

To extract a module into a service you change **three** things — none of them the module's domain code:

1. **Give the module its own host** — a `slice-api` (or `slice-api-minimal`) host whose root module
   `[DependsOn]` just that one module (plus the shared `Contracts`).
2. **Swap the event transport from loopback to a broker.** The default
   `LocalDistributedEventPublisher` (`Slice.EventBus`) dispatches in-process; a transport adapter does
   `services.RemoveAll<IDistributedEventPublisher>()` + registers its own — e.g.
   `AddSliceRabbitMq(...)`, `AddSliceKafka(...)`, `AddSliceAzureServiceBus(...)`, or
   `AddSlicePostgresEventBus()`. The publishing/handling code (`AddDistributedEvent(...)`,
   `IDistributedEventHandler<T>`) is unchanged.
3. **Give the service its own database** — each service owns its data (`AddSliceDbContext<T>`), including
   its slice of the transactional outbox/inbox.

The **`Contracts` project is the inter-service contract**: small records marked with
`[DistributedEventName("...")]`. Keep it additive and versioned (see §3). Everything else about a module
— aggregates, slices, the mediator pipeline, multi-tenancy, permissions — works identically whether it's
one of many modules in a host or the only module in a service.

→ See [Modularity & DI](modularity-and-di.md) and [Messaging & events](messaging.md).

---

## 2. Communication between services

### Async (the default, preferred path)
The framework's backbone is the **transactional outbox → broker → inbox** pipeline:

```
SaveChanges → SliceOutbox row (same transaction)
   → OutboxProcessor → IDistributedEventPublisher (broker transport) → topic/queue
      → consumer → IInboxStore dedup → IDistributedEventHandler<T>
```

- **Transports:** `Slice.EventBus.RabbitMQ`, `Slice.EventBus.AzureServiceBus`, `Slice.EventBus.Kafka`,
  `Slice.EventBus.Postgres` — each verified end-to-end against a real broker (see [Testing](testing.md)).
- **Delivery is at-least-once.** Consumers dedup via `IInboxStore` (`EfInboxStore<TContext>` persists on
  the service's own DB). Design handlers to be idempotent.
- **Retries / dead-letter** are broker concerns (configure the transport), not framework code.
- **Contract versioning:** evolve events **additively** (new optional fields, or a new
  `[DistributedEventName]` for a breaking shape) so old and new consumers coexist. The event-name
  registry decouples the wire name from the CLR type.

### Sync (request/response, when you truly need it)
Expose HTTP (controllers or [minimal APIs](minimal-apis.md)), versioned and HAL-capable, and call it from
another service. **Gap:** Slice has **no typed inter-service client, no resilience pipeline, and no
service discovery.** Bring the platform pieces: `IHttpClientFactory` typed clients +
`Microsoft.Extensions.Http.Resilience` (retry/circuit-breaker/timeout) + discovery (§7). Prefer async
events for cross-service state changes; reserve sync calls for genuine read-time composition.

---

## 3. Transactions across services

There is **no distributed (2-phase) transaction** — and you don't want one. Slice gives you the two
patterns that replace it:

- **Local atomicity + reliable publish — the outbox.** A command commits its data **and** its
  integration events in one local transaction; the `OutboxProcessor` delivers them afterwards. No event
  is lost and none is published without its data committing.
- **Eventual consistency via choreography.** Each service reacts to events and emits its own. The sample
  already does this: `OrderPlacedEto` → Billing creates an invoice → `InvoiceCreatedEto`; Inventory
  reserves stock → `StockReservedEto`; Notifications pushes via SignalR. No central coordinator.

For **multi-step workflows that need compensation** (a saga / process-manager), there is **no built-in
orchestrator (gap).** Two options:

1. **Build it on Slice:** a `Saga` aggregate persists the workflow state; an `IDistributedEventHandler<T>`
   per step advances it and emits the next command/event, issuing **compensating** events on failure.
   You get persistence, the outbox, and idempotency for free.
2. **Adopt a saga library** (MassTransit / NServiceBus) — powerful, but it brings its own bus and
   competes with `IDistributedEventBus`; weigh the overlap.

> Aim for **idempotent, commutative** handlers (inbox dedup) rather than chasing exactly-once delivery,
> which is unachievable end-to-end.

---

## 4. Authentication & authorization across services

### Auth server
Run [`Slice.Authentication`](security.md) (OpenIddict + ASP.NET Identity) as a dedicated **Identity
service** that issues JWTs at `/connect/token`. Resource services **validate** the token rather than
issue it.

> **Gap / config:** the auth module today wires the **server + local validation in one process**
> (`SliceAuthenticationModule.cs` — `.AddServer(...)` then `.AddValidation().UseLocalServer()`). A
> resource service needs **validation-only** wiring — `.AddValidation()` with the issuer set (or
> `JwtBearer` pointed at the Identity service's discovery document). Tracked in the backlog as
> "auth validation-only mode."

### Identity & permission propagation
`HttpCurrentUser` reads the current user/tenant/permissions from the **bearer token's claims**, so
identity flows across services automatically — every service that validates the JWT sees the same
`ICurrentUser`/`ICurrentTenant`. For service-to-service calls, **forward the caller's token**, or use a
**client-credentials** token for system-initiated work.

### Authorization
Each service enforces `[SlicePermission(...)]` via its own `IPermissionChecker`. The design choice is
*where grants live*:

| Approach | Pro | Con |
|---|---|---|
| **Permissions as JWT claims** (issued by the Identity service) | services stay autonomous, no shared DB | revocation waits for token refresh |
| **Shared `Slice.Management` permission DB** | instant grant/revoke (the DB-backed store) | couples services to one DB |

Recommended: **claims for request-time authz** (autonomy) **+ a management service** for administering
grants/settings/features/tenants centrally — see [Permissions](permissions.md) and the management module.

---

## 5. The menu / application-configuration question (yes — your single host needs it too)

A frontend needs to know, per signed-in user, **what to render and what to gate**: granted permissions,
effective settings, enabled features, localized strings, the **navigation menu**, and the current
user/tenant. (ABP exposes this as one "application-configuration" endpoint.)

> **Gap — and it applies equally to a single-host app:** Slice has **no navigation/menu provider and no
> aggregated app-config endpoint** today. A monolith frontend hits the same wall — it still needs one
> call that says "here's this user's permissions + menu + settings."

Recommended shape (backlog item):

- An `IMenuContributor` / navigation provider (each module contributes menu items, permission-gated like
  the HAL [link contributors](hypermedia-and-caching.md)).
- An `/api/app-config` endpoint returning `{ currentUser, currentTenant, grantedPermissions, settings,
  features, localization, menu }`.

In a **monolith** that's a single endpoint. In **microservices** the menu/config is spread across
services, so compose it in a **BFF/gateway** (§7) — each service contributes its menu items and the
gateway merges them for the frontend.

---

## 6. Multi-tenancy across services

Tenant resolution already works across a distributed system: the tenant is carried in the JWT
(`tenant_id` claim) or the `X-Tenant-Id` header and re-established per request by `UseSliceMultiTenancy`
in each service (see [Multi-tenancy](multitenancy.md)). Propagate it the same way you propagate identity
(forward the header/token). Each service can independently choose row-level isolation or
**database-per-tenant**, and run the [tenant database migrator](multitenancy.md#migrating-tenant-databases)
as its own deploy job.

---

## 7. Cross-cutting infrastructure — platform, not framework

These are deliberately **not** Slice's job; bring proven platform components:

| Concern | Recommended |
|---|---|
| **Service discovery** | .NET Aspire (local/dev), Kubernetes DNS / a service mesh (prod) |
| **API gateway / reverse proxy / BFF** | **YARP** — routing, TLS, auth offload, and **app-config/menu aggregation** (§5); or a cloud gateway (APIM, etc.) |
| **Load balancing** | Kubernetes `Service`/`Ingress`, or a cloud load balancer |
| **Local orchestration / DX** | a **.NET Aspire AppHost** to run the services + broker + databases together |
| **Config & secrets** | per-service `appsettings` + env vars + a secret store (Key Vault / k8s Secrets) |

The gateway/BFF is also the natural home for the **frontend app-config composition** and for
cross-service read aggregation.

---

## 8. Production-grade checklist

Slice gives you the application building blocks; production-readiness layers on top (several items are
tracked in the [backlog](#issue-ready-backlog)):

- **Persistent keys** — Data Protection key ring + OpenIddict signing/encryption keys in a shared store
  (ephemeral keys break multi-instance auth). *(gap)*
- **Health checks** — liveness + readiness per service, including a DB-reachability probe. *(gap)*
- **Observability** — OpenTelemetry tracing + metrics with trace correlation across services; without it,
  cross-service latency and queue backlog are invisible. **Essential** for distributed systems. *(gap)*
- **Migrations as deploy jobs** — run EF migrations as a separate step before rollout (the
  [tenant-migrator / `--migrations job`](multitenancy.md#migrating-tenant-databases) pattern), not at
  serving-host startup.
- **Resilience** — retry/circuit-breaker on sync calls (`Microsoft.Extensions.Http.Resilience`) + broker
  retry/dead-letter on async.
- **Idempotency** — the inbox (`IInboxStore`) dedups at-least-once delivery.
- **Versioning** — API versioning ([Cross-cutting services](cross-cutting-services.md)) + additive event
  contracts (§3).
- **Rate limiting** — ASP.NET Core's `RateLimiter` middleware (no framework wrapper yet). *(gap)*

---

## 9. What Slice gives you vs what you add

| Concern | Slice provides | You add |
|---|---|---|
| Service boundary | `SliceModule` bounded contexts | — |
| Async comms | outbox + inbox + RabbitMQ/Kafka/Azure SB/Postgres transports | broker infra |
| Sync comms | controllers / minimal APIs, versioning, HAL | typed client + resilience *(gap)* |
| Transactions | transactional outbox, choreography | saga/orchestration *(gap)* or a library |
| Auth (issue) | OpenIddict Identity service | validation-only mode for resource services *(gap)* |
| AuthZ | `[SlicePermission]`, DB-backed grants, claims | grant-distribution policy |
| Identity/tenant propagation | JWT claims via `HttpCurrentUser` / `ICurrentTenant` | token forwarding |
| Frontend menu / app-config | — | app-config + menu provider *(gap)*; BFF aggregation |
| Discovery / gateway / LB | — | Aspire / k8s / YARP *(platform)* |
| Keys / health / observability | — | *(gaps — see backlog)* |
| Per-service data + migrations | EF + outbox/inbox, DB-per-tenant, migrator | — |

---

## Issue-ready backlog

Framework gaps this guide surfaces, ready to become issues (the first three were drafted earlier; the
keys/health/observability and MSSQL/MySQL items are tracked separately):

1. **Application-config + menu/navigation endpoint** — `IMenuContributor` + `/api/app-config`
   (granted permissions + settings + features + localization + menu + current user/tenant). Needed by
   **both** monolith and microservices; aggregate via BFF in the distributed case.
2. **Auth validation-only mode** — a resource-service wiring for `Slice.Authentication` that validates
   JWTs against a remote Identity service (no `.AddServer`/`UseLocalServer`).
3. **Saga / process-manager helper** — a persisted workflow-state aggregate + step/compensation
   handlers on top of the outbox (or documented guidance for adopting MassTransit/NServiceBus).
4. **Inter-service HTTP client + resilience** — typed client guidance/wrapper over `IHttpClientFactory`
   + `Microsoft.Extensions.Http.Resilience`, with identity/tenant propagation.
5. **Rate limiting** — a thin wiring over ASP.NET Core's `RateLimiter`.
6. **Samples (optional)** — a .NET Aspire AppHost + a YARP BFF that extracts the modular-monolith into
   two services over a real broker.
7. **Cross-ref:** persistent keys, health checks, observability, first-class SQL Server/MySQL (drafted
   separately).

> Net: you can build microservices on Slice **today** — the module model, outbox/broker messaging,
> token-based identity, and per-service data are the hard parts and they're done. The gaps above are
> additive (frontend config/menu, auth validation split, saga helpers) or belong to the platform
> (discovery, gateway, load balancing).
