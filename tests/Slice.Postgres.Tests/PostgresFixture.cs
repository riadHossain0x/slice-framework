using Testcontainers.PostgreSql;

namespace Slice.Postgres.Tests;

/// <summary>
/// One Postgres container shared by every ADO adapter test class (cache, lock, event bus, jobs, blob,
/// EF). Each test ensures its own (idempotent) schema, so they coexist on one database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
