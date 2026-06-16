using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector;
using Slice.Core.Ambient;
using Slice.Postgres;
using Slice.Vector;

namespace Slice.Vector.Postgres;

/// <summary>An <see cref="IVectorStore"/> backed by PostgreSQL + the <c>pgvector</c> extension.</summary>
public sealed class PostgresVectorStore(NpgsqlDataSource dataSource, ICurrentTenant currentTenant) : IVectorStore
{
    public IVectorCollection GetCollection(string name, int dimensions, VectorDistance distance = VectorDistance.Cosine)
        => new PostgresVectorCollection(dataSource, currentTenant, name, dimensions, distance);
}

/// <summary>One table per collection (<c>slice_vec_{name}</c>) with an HNSW index for the chosen metric.</summary>
public sealed class PostgresVectorCollection(
    NpgsqlDataSource dataSource, ICurrentTenant currentTenant, string name, int dimensions, VectorDistance distance)
    : IVectorCollection
{
    public string Name { get; } = name;
    public int Dimensions { get; } = dimensions;
    public VectorDistance Distance { get; } = distance;

    private readonly string _table = "slice_vec_" + Sanitize(name);

    private string Operator => Distance switch
    {
        VectorDistance.Cosine => "<=>",
        VectorDistance.L2 => "<->",
        VectorDistance.InnerProduct => "<#>",
        _ => "<=>"
    };

    private string OpClass => Distance switch
    {
        VectorDistance.Cosine => "vector_cosine_ops",
        VectorDistance.L2 => "vector_l2_ops",
        VectorDistance.InnerProduct => "vector_ip_ops",
        _ => "vector_cosine_ops"
    };

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE IF NOT EXISTS {_table} (
                id        text PRIMARY KEY,
                embedding vector({Dimensions}) NOT NULL,
                content   text NULL,
                metadata  jsonb NULL,
                tenant_id uuid NULL
            );
            CREATE INDEX IF NOT EXISTS {_table}_embedding_idx ON {_table}
                USING hnsw (embedding {OpClass});
            CREATE INDEX IF NOT EXISTS {_table}_tenant_idx ON {_table} (tenant_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        // The 'vector' type only exists once the extension is installed; refresh the data source's type
        // catalog so subsequent commands can bind Pgvector.Vector parameters.
        await dataSource.ReloadTypesAsync(ct);
    }

    public Task UpsertAsync(VectorRecord record, CancellationToken ct = default)
        => UpsertBatchAsync([record], ct);

    public async Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        foreach (var record in records)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_table} (id, embedding, content, metadata, tenant_id)
                VALUES (@id, @emb, @content, @meta, @tid)
                ON CONFLICT (id) DO UPDATE
                  SET embedding = EXCLUDED.embedding, content = EXCLUDED.content,
                      metadata = EXCLUDED.metadata, tenant_id = EXCLUDED.tenant_id
                """;
            cmd.Parameters.AddWithValue("id", record.Id);
            cmd.Parameters.AddWithValue("emb", new Pgvector.Vector(record.Embedding));
            cmd.Parameters.AddWithValue("content", (object?)record.Content ?? DBNull.Value);
            cmd.Parameters.Add(new NpgsqlParameter("meta", NpgsqlTypes.NpgsqlDbType.Jsonb)
            {
                Value = record.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(record.Metadata)
            });
            cmd.Parameters.AddWithValue("tid", (object?)(record.TenantId ?? currentTenant.Id) ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_table} WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] query, int topK, CancellationToken ct = default)
    {
        var sql = new StringBuilder($"SELECT id, embedding, content, metadata, tenant_id, embedding {Operator} @q AS dist FROM {_table}");
        var tenantId = currentTenant.Id;
        if (tenantId is not null)
            sql.Append(" WHERE tenant_id = @tid");
        sql.Append($" ORDER BY embedding {Operator} @q LIMIT @k");

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("q", new Pgvector.Vector(query));
        cmd.Parameters.AddWithValue("k", topK);
        if (tenantId is not null)
            cmd.Parameters.AddWithValue("tid", tenantId.Value);

        var results = new List<VectorSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var embedding = reader.GetFieldValue<Pgvector.Vector>(1).ToArray();
            var content = reader.IsDBNull(2) ? null : reader.GetString(2);
            var metadata = reader.IsDBNull(3)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(3));
            var tid = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
            var score = reader.GetDouble(5);
            results.Add(new VectorSearchResult(new VectorRecord(reader.GetString(0), embedding, content, metadata, tid), score));
        }
        return results;
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        return sb.ToString();
    }
}

public static class PostgresVectorStoreRegistration
{
    /// <summary>Registers the pgvector-backed <see cref="IVectorStore"/>. Pass a connection string to
    /// build the shared data source with <c>UseVector()</c> here; otherwise ensure the data source was
    /// built with vector support (e.g. via <c>AddSlicePostgresStack</c>).</summary>
    public static IServiceCollection AddSlicePostgresVectorStore(
        this IServiceCollection services, string? connectionString = null)
    {
        if (connectionString is not null)
            services.AddSlicePostgres(connectionString, b => b.UseVector());

        services.AddSingleton<IVectorStore, PostgresVectorStore>();
        return services;
    }
}
