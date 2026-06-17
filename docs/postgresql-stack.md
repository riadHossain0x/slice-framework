# The PostgreSQL stack — run everything on Postgres

Slice exposes every infrastructure concern as a [seam with a swappable adapter](architecture.md#seams-and-adapters).
The **PostgreSQL stack** is a suite of adapters that put *all* of those seams on a single Postgres
database — caching, distributed locking, the distributed event bus, durable background jobs, blob
storage, vector search, and the EF data itself (outbox/inbox, auth, management). A product built on
Slice can then run on **one managed dependency**.

```csharp
// the whole stack on one shared connection pool…
builder.Services.AddSlicePostgresStack(connectionString);
// …and EF data + outbox/inbox on the same pool
builder.Services.AddSliceDbContext<AppDbContext>((sp, o) =>
    o.UseSlicePostgres(sp.GetRequiredService<NpgsqlDataSource>()));
```

| Concern | Seam | Postgres adapter | How it's stored |
|---|---|---|---|
| Cache | `IDistributedCache` | `Slice.Caching.Postgres` | `slice_cache` table + sweeper |
| Distributed lock | `IDistributedLock` | `Slice.DistributedLocking.Postgres` | session advisory locks |
| Event bus | `IDistributedEventPublisher` | `Slice.EventBus.Postgres` | `slice_event_queue` + `LISTEN/NOTIFY` |
| Background jobs | `IBackgroundJobManager` | `Slice.BackgroundJobs.Postgres` | `slice_jobs` / `slice_recurring_jobs` |
| Blob storage | `IBlobProvider` | `Slice.BlobStoring.Postgres` | `slice_blobs` (`bytea`) |
| Vector search | `IVectorStore` *(new)* | `Slice.Vector.Postgres` | `slice_vec_*` (pgvector) |
| Embeddings | `IEmbeddingGenerator` *(new)* | `Slice.Embeddings.OpenAI` | — (calls a model) |
| EF data + outbox/inbox/auth/management | EF Core | `Slice.EntityFrameworkCore.PostgreSQL` | your tables + `SliceOutbox`/`SliceInbox` |

---

## À la carte — use any adapter on its own

The "stack" is a convenience, not a requirement. Each adapter is an independent opt-in package that
depends only on **`Slice.Postgres`** (the shared data source) plus its own seam abstraction — **none of
them depends on `Slice.PostgresStack`**. So you can adopt a single Postgres-backed seam without the rest,
and freely mix backends (e.g. EF on SQL Server, the event bus on Postgres, the cache on Redis):

```csharp
// event bus only — reference Slice.Postgres + Slice.EventBus.Postgres
services.AddSlicePostgres(connectionString);   // the shared NpgsqlDataSource pool
services.AddSlicePostgresEventBus();           // reuses it — no Slice.PostgresStack needed
```

Each adapter exposes its own `AddSlicePostgresXxx()` (and accepts an optional connection string to
register the pool itself). `Slice.PostgresStack` simply calls these for you — see
[The one-call stack](#the-one-call-stack--slicepostgresstack) below.

---

## One shared connection pool

The foundation package **`Slice.Postgres`** registers a single `NpgsqlDataSource` (one connection
pool) and a schema initializer. Every adapter reuses that data source, so the full stack does not open
N independent pools.

```csharp
// registers NpgsqlDataSource (TryAddSingleton — first caller wins) + the schema initializer
services.AddSlicePostgres(connectionString, builder => builder.UseVector() /* for pgvector */);
```

Each adapter's `AddSlicePostgresXxx(...)` extension:

- takes an **optional connection string** — pass one to register the shared data source here, or omit
  it to reuse one already registered (e.g. by `AddSlicePostgresStack`);
- swaps the framework default with `services.RemoveAll<TSeam>()` + `services.Add…<TSeam, TImpl>()`;
- contributes its idempotent DDL via `AddPostgresSchema(ddl)`.

`PostgresSchemaInitializer` runs every contributor's `CREATE TABLE IF NOT EXISTS …` once at startup,
guarded by a **session advisory lock** so concurrent instances don't race.

---

## The adapters

### Caching — `Slice.Caching.Postgres`
`PostgresDistributedCache : IDistributedCache` stores entries in `slice_cache(key, value bytea,
expires_at, sliding_seconds, absolute_expires_at)`; a `PostgresCacheSweeper` deletes expired rows each
minute. Absolute and sliding expirations are honoured. `ISliceCache` (typed, tenant-aware) rides on
top unchanged.

```csharp
services.AddSlicePostgresCache();   // when the data source is already registered (e.g. by the stack)
```

### Distributed locking — `Slice.DistributedLocking.Postgres`
`PostgresDistributedLock` uses PostgreSQL **session advisory locks**: it hashes the key
(`hashtextextended`), holds a dedicated connection, retries `pg_try_advisory_lock` until the timeout,
and releases on dispose. A crashed holder's lock is released automatically when its session ends — no
TTL needed (unlike the Redis lock).

```csharp
await using var handle = await distributedLock.TryAcquireAsync("outbox:app", TimeSpan.FromSeconds(30), ct);
if (handle is not null) { /* exclusive section */ }
```

### Event bus — `Slice.EventBus.Postgres`
`PostgresEventPublisher` inserts each integration event into `slice_event_queue` and fires
`NOTIFY slice_events`. `PostgresEventConsumer` (a `BackgroundService`) keeps a `LISTEN` connection for
low-latency wake-ups **and** polls as a durable fallback, claiming rows with `FOR UPDATE SKIP LOCKED`
(batch 50) and dispatching each to local handlers via `IDistributedEventConsumer.ConsumeAsync`.

Delivery is **at-least-once**; rely on the inbox (`IInboxStore` / `EfInboxStore`) for consumer-side
dedup. NOTIFY is a latency optimisation layered on the durable queue (NOTIFY alone is not durable).

It pairs naturally with the EF outbox:

```
SaveChanges → SliceOutbox row (same transaction)
   → OutboxProcessor → PostgresEventPublisher → slice_event_queue + NOTIFY
      → PostgresEventConsumer → IDistributedEventHandler<T>
```

### Background jobs — `Slice.BackgroundJobs.Postgres`
A durable queue: `EnqueueAsync` writes a row to `slice_jobs` (args as `jsonb`, the type as the args'
assembly-qualified name); `PostgresJobWorker` claims due rows with `FOR UPDATE SKIP LOCKED`,
rehydrates the args type, resolves `IBackgroundJob<TArgs>`, and runs it — marking it done or bumping
`retry_count` with exponential backoff. `PostgresRecurringScheduler` enqueues due entries from
`slice_recurring_jobs`. Survives restarts (jobs live in the table).

### Blob storage — `Slice.BlobStoring.Postgres`
`PostgresBlobProvider : IBlobProvider` stores blobs as `bytea` in `slice_blobs(container, name, data)`.
Typed `IBlobContainer<TContainer>` markers work unchanged on top.

### EF data on Postgres — `Slice.EntityFrameworkCore.PostgreSQL`
`UseSlicePostgres(options, dataSource)` (and a connection-string overload) configures a
`SliceDbContext` to run on Postgres via Npgsql. So your aggregates, the transactional outbox/inbox,
and the auth/management stores all run on Postgres too. Pair it with the `IServiceProvider`-aware
`AddSliceDbContext<T>((sp, o) => …)` overload to reuse the stack's pool.

---

## Vector search & embeddings

`Slice.Vector` adds a new seam for semantic search, independent of Postgres:

- `IVectorStore.GetCollection(name, dimensions, distance)` → an `IVectorCollection`
  (`EnsureCreatedAsync`, `UpsertAsync`/`UpsertBatchAsync`, `DeleteAsync`, `SearchAsync(query, topK)`).
- `VectorRecord(Id, float[] Embedding, Content?, Metadata?, TenantId?)`,
  `VectorSearchResult(Record, Score)`, `VectorDistance { Cosine, L2, InnerProduct }`.
- `IEmbeddingGenerator` turns text into vectors. The default `HashingEmbeddingGenerator` is
  **deterministic and offline** (feature-hashing, 256 dims) — great for dev/tests, not semantic.

`Slice.Vector.Postgres` implements the store on **pgvector**: one table per collection
(`slice_vec_{name}`) with an HNSW index for the chosen metric (`<=>` cosine, `<->` L2, `<#>` inner
product), tenant-filtered by `ICurrentTenant`. The store accepts raw `float[]`, so embeddings are
optional.

`Slice.Embeddings.OpenAI` provides a real generator that calls an OpenAI-compatible `/v1/embeddings`
endpoint — one adapter covers **OpenAI, Azure OpenAI, Ollama and LM Studio** (just set `BaseUrl`).

```csharp
services.AddSlicePostgresVectorStore();                       // pgvector store
services.AddSliceOpenAiEmbeddings(o =>                         // optional real embedder
{
    o.BaseUrl = "http://localhost:11434";                     // e.g. Ollama
    o.Model = "nomic-embed-text";
    o.Dimensions = 768;
});

// in a handler:
var collection = vectors.GetCollection("notes", embedder.Dimensions);
await collection.EnsureCreatedAsync(ct);
await collection.UpsertAsync(new VectorRecord(id, await embedder.GenerateAsync(text, ct), text), ct);
var hits = await collection.SearchAsync(await embedder.GenerateAsync(query, ct), topK: 5, ct);
```

> **pgvector gotcha (handled for you):** the `vector` type only exists after `CREATE EXTENSION vector`,
> so `EnsureCreatedAsync` reloads the data source's type catalog (`ReloadTypesAsync`) after creating
> the extension, and the data source must be built with `UseVector()` (the connection-string overload
> and the stack do this automatically).

---

## The one-call stack — `Slice.PostgresStack`

`AddSlicePostgresStack(connectionString, configure?)` builds the single shared `NpgsqlDataSource`
(with `UseVector()` when the vector store is enabled) and wires every adapter on it. Each part is
toggleable via `PostgresStackOptions` (all on by default). It is purely a convenience aggregator — it
just calls `AddSlicePostgres(connectionString)` followed by each adapter's `AddSlicePostgresXxx()`; you
can make those same calls yourself (see [À la carte](#à-la-carte--use-any-adapter-on-its-own)) if you
only want some of them.

```csharp
builder.Services.AddSlicePostgresStack(connectionString, o =>
{
    o.BlobStoring = false;   // e.g. keep blobs on S3
});
builder.Services.AddSliceDbContext<AppDbContext>((sp, o) =>
    o.UseSlicePostgres(sp.GetRequiredService<NpgsqlDataSource>()));
```

See the runnable **`samples/Slice.Sample.PostgresStack`** (with a `docker-compose.yml` for
`pgvector/pgvector:pg16`): one request creates a note → persists on Postgres → the integration event
flows outbox → PG event bus → handler → a background job runs → the note is indexed for vector search.

---

## Scaffolding a Postgres app

The `slice-api` template takes a `--database postgres` switch:

```bash
dotnet new slice-api -n Acme.Shop --database postgres
cd Acme.Shop
docker compose up -d          # pgvector/pgvector:pg16
dotnet run                    # auth, management, outbox, data — all on Postgres
```

It wires `AddSlicePostgresStack` + `UseSlicePostgres` and ships a `docker-compose.yml`. (The default,
without the switch, stays on zero-dependency SQLite.)

---

## Operational notes

- **Multiple DbContexts on one database:** EF Core's `EnsureCreated` is all-or-nothing *per database* —
  the first context to run creates its tables and the rest are skipped. The app data + stack share the
  main database (the app context's `EnsureCreated` runs first on the empty DB; the stack's `CREATE
  TABLE IF NOT EXISTS` DDL is additive), but the **auth and management contexts get their own
  databases** on the same server (the template appends `_auth`/`_mgmt`). For a single database with
  schemas, switch to EF **migrations** (recommended for production) instead of `EnsureCreated`.
- **Indexes & retention:** the cache sweeper and the event/job queues have partial indexes on the
  unprocessed/expired rows; consider periodic archival of processed `slice_event_queue`/`slice_jobs`
  rows for very high throughput.
- **Connection budget:** the advisory-lock adapter holds a dedicated connection per live lock handle;
  size the pool (`Maximum Pool Size` in the connection string) accordingly.
- **pgvector index tuning:** HNSW build/query parameters and `IVFFlat` vs `HNSW` are workload-specific;
  the adapter defaults to HNSW with the op-class matching the chosen `VectorDistance`.
