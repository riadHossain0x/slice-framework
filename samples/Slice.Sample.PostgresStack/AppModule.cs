using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Slice.AspNetCore;
using Slice.BackgroundJobs;
using Slice.Caching;
using Slice.EntityFrameworkCore;
using Slice.EntityFrameworkCore.PostgreSQL;
using Slice.EventBus;
using Slice.Mediator.Default;
using Slice.Modularity;
using Slice.MultiTenancy;
using Slice.PostgresStack;
using Slice.Vector;

namespace Slice.Sample.PostgresStack;

/// <summary>
/// One bounded context running entirely on PostgreSQL: EF data, the transactional outbox/inbox, the
/// distributed event bus, durable background jobs, caching, blob storage and pgvector — all on a single
/// shared connection pool via <c>AddSlicePostgresStack</c> + <c>UseSlicePostgres</c>.
/// </summary>
[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(SliceEntityFrameworkCoreModule),
    typeof(SliceMultiTenancyModule),
    typeof(SliceCachingModule),
    typeof(SliceBackgroundJobsModule),
    typeof(SliceVectorModule))]
public sealed class AppModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(AppModule).Assembly;
        var connectionString = context.Configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=slice;Username=postgres;Password=postgres";

        services.AddSliceMediator();
        services.AddRequestHandlers(assembly);
        services.AddDistributedEvents(assembly);          // [DistributedEventName] → type registry
        services.AddDistributedEventHandlers(assembly);
        services.AddBackgroundJobHandlers(assembly);
        services.AddValidatorsFromAssembly(assembly);
        services.AddSliceConventions(assembly);

        // The whole Postgres stack on one pool…
        services.AddSlicePostgresStack(connectionString);
        // …and EF (data + outbox + inbox) reusing that same pool.
        services.AddSliceDbContext<AppDbContext>((sp, o) =>
            o.UseSlicePostgres(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<INoteRepository, EfNoteRepository>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

        // Provision the vector collection up front (CREATE EXTENSION vector + table + HNSW index).
        var embedder = sp.GetRequiredService<IEmbeddingGenerator>();
        await sp.GetRequiredService<IVectorStore>()
            .GetCollection(NotesVector.Collection, embedder.Dimensions)
            .EnsureCreatedAsync();
    }
}
