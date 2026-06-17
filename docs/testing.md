# Testing

Slice is built test-first. The suite mixes fast in-process tests with real-infrastructure integration
tests (Testcontainers) for the parts that can only be trusted against a real broker/store.

---

## The test suite

| Project | Kind | Covers |
|---|---|---|
| `Slice.Domain.Tests` | unit | aggregates, value objects, specifications, `Ensure` guards (11 tests) |
| `Slice.Mediator.Conformance.Tests` | unit | both mediator engines behave identically (3 tests) |
| `Slice.Architecture.Tests` | architecture (NetArchTest) | Domain has no infra deps; slices don't reference each other; controllers inherit `SliceController` (5 tests) |
| `Slice.Templates.Tests` | structural (no build) | `dotnet new` templates: valid manifests, unique identities/short-names, all templates packaged, runnable templates ship a `nuget.config`, projects reference the framework as `SLICE_VERSION` packages (not `src/` project refs) |
| `Slice.Data.Tests` | integration (SQLite) | EF + Dapper + LinqToDB share one connection/transaction; database-per-tenant isolation (2 tests) |
| `Slice.Serilog.Tests` | integration (in-memory sink) | logging routes through Serilog + LogContext enrichment (1 test) |
| `Slice.AspNetCore.SignalR.Tests` | integration (Kestrel + client) | hub invoke + server push (1 test) |
| `Slice.EventBus.RabbitMQ.Tests` | integration (**Docker**) | publish → broker → handler over RabbitMQ (1 test) |
| `Slice.EventBus.Kafka.Tests` | integration (**Docker**) | publish → broker → handler over Kafka (1 test) |
| `Slice.BlobStoring.Minio.Tests` | integration (**Docker**) | save/exists/read/delete against MinIO (1 test) |
| `Slice.Emailing.MailKit.Tests` | unit + integration (**Docker**) | MIME building; send → received via Mailpit (3 tests) |

### Running

```bash
# everything (Docker required for the broker/store tests)
dotnet test Slice.slnx

# fast subset only (no Docker)
dotnet test Slice.slnx \
  --filter "FullyQualifiedName!~RabbitMQ&FullyQualifiedName!~Kafka&FullyQualifiedName!~Minio&FullyQualifiedName!~MailKit"
```

Stack: **xUnit** (v2), **NetArchTest** for architecture rules, **Testcontainers** for real
infrastructure.

### Template coverage

`Slice.Templates.Tests` runs with the normal suite and statically validates the `dotnet new` templates
(manifests, packaging, package-reference conventions) without building anything. The heavy end-to-end
check — pack the framework + templates, install them, then scaffold **and build** every template
(including both `slice-tenant-api --migrations host|job` modes) — lives in a script (needs the network):

```bash
eng/smoke-templates.sh
```

---

## Pattern 1 — composing modules in-process

For anything that needs the DI graph (DbContext, behaviors, event bus), boot a real host from a small
test module instead of hand-registering services. This is exactly how the app runs, so the test
exercises the real wiring.

```csharp
[DependsOn(typeof(SliceEntityFrameworkCoreModule), typeof(SliceDapperModule), typeof(SliceLinqToDbModule))]
public sealed class DataTestModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var cs = context.Configuration.GetConnectionString("Test")!;
        context.Services.AddSliceDbContext<TestDbContext>(o => o.UseSqlite(cs));
        context.Services.AddSliceLinqToDb<TestDbContext>(SQLiteTools.GetDataProvider(ProviderName.SQLiteMS));
    }
}

var builder = Host.CreateApplicationBuilder();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:Test"] = $"Data Source={dbPath}"
});
builder.Services.AddSliceModules<DataTestModule>(builder.Configuration);
var host = builder.Build();
await host.Services.InitializeSliceModulesAsync();

using var scope = host.Services.CreateScope();
var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
await ctx.Database.EnsureCreatedAsync();
```

The shared-connection test then begins a transaction on the context, writes through EF, reads the same
rows through `IDapperExecutor<TestDbContext>` and `ISliceDataConnectionFactory<TestDbContext>`, and
rolls back — proving all three ORMs share the unit of work.

The database-per-tenant test switches `ICurrentTenant.Change(tenantId)` around each scope and asserts
each tenant's writes land in its own SQLite file.

---

## Pattern 2 — Testcontainers for real infrastructure

Broker/store behaviour (acks, consumer groups, S3 semantics, SMTP) can't be faithfully faked, so those
tests spin up the real service in Docker via `IAsyncLifetime`:

```csharp
public sealed class MinioBlobProviderTests : IAsyncLifetime
{
    private readonly MinioContainer _minio = new MinioBuilder().Build();
    public Task InitializeAsync() => _minio.StartAsync();
    public Task DisposeAsync() => _minio.DisposeAsync().AsTask();

    [Fact]
    public async Task Save_exists_read_and_delete_round_trip()
    {
        var services = new ServiceCollection();
        services.AddSliceBlobStoringMinio(o =>
        {
            o.Endpoint = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}";
            o.AccessKey = _minio.GetAccessKey();
            o.SecretKey = _minio.GetSecretKey();
            o.Bucket = "slice-test";
        });
        var provider = services.BuildServiceProvider().GetRequiredService<IBlobProvider>();
        // … save → exists → read-back → delete …
    }
}
```

The event-bus tests follow the same shape: start the broker, build a host with
`AddSliceConventions(...)` + `AddDistributedEvents(...)` + the transport (`AddSliceRabbitMq` /
`AddSliceKafkaEventBus`), publish, and await a `TaskCompletionSource` the handler completes.

### Gotchas worth knowing

- **Apple Silicon / arm64:** the default `confluentinc/cp-kafka` tag has no arm64 manifest. The Kafka
  test pins a multi-arch tag: `new KafkaBuilder().WithImage("confluentinc/cp-kafka:7.7.1")`.
- **Kafka timing:** consumer-group rebalancing takes seconds; the test uses `AutoOffsetReset.Earliest`
  and a generous timeout so order of publish vs. subscribe doesn't matter.
- **Serilog InMemory sink:** don't call `Log.CloseAndFlush()` before asserting — disposing the logger
  disposes (clears) the in-memory sink. The sink is synchronous, so the event is already captured.
- **xUnit v2:** there is no `TestContext.Current`; use `CancellationToken.None` (or your own CTS) in
  test bodies.

---

## Testing a vertical slice

Because a slice is just a request + handler, you can test the handler directly (pure, fast) and/or
through the controller via `WebApplicationFactory` for the full pipeline.

**Handler-level (no host):**

```csharp
var repo = new FakeLeadRepository();
var handler = new CreateLeadHandler(repo, new SequentialGuidGenerator(), new NullCurrentTenant());
var result = await handler.HandleAsync(new CreateLeadCommand("Ada", "Lovelace", "ada@x.com"), default);
Assert.True(result.IsSuccess);
```

**Pipeline-level (through the mediator):** boot a test module that depends on
`SliceApplicationModule` + your bounded context, resolve `ISender`, and send the command — validation,
authorization, and the unit of work all run. Use a `NullCurrentUser`/`NullCurrentTenant` or push a
principal/tenant to exercise the cross-cutting behaviors.

---

## Architecture tests

`Slice.Architecture.Tests` encodes the rules that keep the design honest (NetArchTest):

- `Slice.Domain` must not depend on EF Core, ASP.NET, or other infrastructure.
- Feature slices must not reference each other (slices are independent).
- Controllers must inherit `SliceController`.

Run them in CI to catch layering violations before they spread.
