# Slice sample — Modular monolith

A **modular monolith**: four bounded-context modules in **one process**, each owning its **own SQLite
database** and using a **different data-access stack**, communicating **only through integration
events** on the in-process distributed event bus. No module references another module's internals —
they share only the contracts in `Slice.Sample.Monolith.Contracts`.

```
POST /api/orders            (Sales · EF · sales.db)
  └─ Order raises OrderPlacedEto ──► transactional outbox ──► in-process event bus
        ├─ Billing   (LinqToDB · billing.db)   inserts an Invoice  ─► InvoiceCreatedEto
        └─ Inventory (Dapper   · inventory.db) reserves stock      ─► StockReservedEto
              └─ Notifications (no DB) logs + pushes both over SignalR (/hubs/notifications)
```

| Project | Bounded context | Stack | Database |
|---|---|---|---|
| `…Contracts` | shared integration events | — | — |
| `…Sales` | placing orders | **EF Core** + repository | `sales.db` |
| `…Billing` | invoicing | **LinqToDB** | `billing.db` |
| `…Inventory` | stock reservation | **Dapper** | `inventory.db` |
| `…Notifications` | customer notifications | event handlers + **SignalR** (`Slice.AspNetCore.SignalR`) | — |
| `…Host` | composition root (`HostModule` `[DependsOn(all four + AspNetCore)]`) | web | — |

Each module is a `SliceModule` that registers its own mediator/handlers, its own `DbContext`
(`AddSliceDbContext` → its own `OutboxProcessor`), and its event handlers. The host just declares the
four modules as dependencies; the loader composes them.

## Run it

```bash
dotnet run --project samples/modular-monolith/Slice.Sample.Monolith.Host

# place an order — watch the console for the cross-module reactions
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customer":"Ada","sku":"BOOK-1","quantity":2}'

curl http://localhost:5000/api/orders        # EF       (sales.db)
curl http://localhost:5000/api/invoices       # LinqToDB (billing.db)
curl http://localhost:5000/api/stock/BOOK-1    # Dapper   (inventory.db)
```

### Extra properties (schema-less data)

`Order` is an `AggregateRoot`, so it gets an `ExtraProperties` JSON column for free. The place-order
request takes optional ad-hoc fields stored without a schema change, and you can filter on them
server-side (`WhereExtraProperty` → SQLite `json_extract`):

```bash
curl -X POST http://localhost:5000/api/orders -H "Content-Type: application/json" \
  -d '{"customer":"Ada","sku":"BOOK-1","quantity":2,"channel":"web","giftNote":"Happy Birthday"}'

curl http://localhost:5000/api/orders/by-channel/web   # only orders whose ExtraProperties.channel == "web"
```

The log shows `BILLING invoice … (via LinqToDB)` → `NOTIFY customer` → `INVENTORY reserved … (via
Dapper)` → `NOTIFY customer`, and three separate `.db` files appear in the working directory.

### Real-time notifications (SignalR)

The Notifications module also **pushes** each notification to connected clients over a SignalR hub at
`/hubs/notifications` (a `NotificationsHub : SliceHub`). Connect a client, listen for the
`notification` client method, then place an order — you receive two live messages (invoice + stock):

```js
const conn = new signalR.HubConnectionBuilder().withUrl("http://localhost:5000/hubs/notifications").build();
conn.on("notification", msg => console.log("RECEIVED:", msg));
await conn.start();
// → "Invoice … is ready for order …"  and  "2× BOOK-1 reserved for order …"
```

Broadcasts go to all clients here (the sample has no auth); with authentication you'd scope by
`SliceHub` groups keyed off `CurrentUserId`/`CurrentTenantId`.

## What it demonstrates

- **Multiple `DbContext`s in one host** — each with its own database + outbox processor, no conflict.
- **Different data-access stacks side by side** — EF, LinqToDB and Dapper, each over its own `SliceDbContext`.
- **Cross-module choreography via events** — Sales uses the aggregate → outbox path; downstream modules
  react and publish follow-on events directly via `IDistributedEventBus`. Swapping the in-process bus
  for a real broker (RabbitMQ/Kafka/Postgres) would split this into microservices unchanged.

See also: [docs/modularity-and-di.md](../../docs/modularity-and-di.md), [docs/messaging.md](../../docs/messaging.md).
