using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slice.AspNetCore;
using Slice.Authentication;
using Slice.Authorization;
using Slice.EntityFrameworkCore;
#if (Postgres)
using Npgsql;
using Slice.EntityFrameworkCore.PostgreSQL;
using Slice.PostgresStack;
#endif
using Slice.EventBus;
using Slice.Localization;
using Slice.Management;
using Slice.Mediator.Default;
using Slice.Modularity;
using Slice.MultiTenancy;
using Slice.Settings;
using SliceApp.Domain;
using SliceApp.Persistence;

namespace SliceApp;

/// <summary>
/// Root application module. Self-registers handlers/validators/conventions, picks the default
/// mediator engine, and wires the application + auth + management DbContexts.
/// </summary>
[DependsOn(
    typeof(SliceAspNetCoreModule),
    typeof(SliceEntityFrameworkCoreModule),
    typeof(SliceMultiTenancyModule),
    typeof(SliceAuthorizationModule),
    typeof(SliceAuthenticationModule),
    typeof(SliceSettingsModule),
    typeof(SliceLocalizationModule),
    typeof(SliceManagementModule))]
public sealed class AppModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var assembly = typeof(AppModule).Assembly;

        services.AddSliceMediator();
        services.AddRequestHandlers(assembly);
        services.AddDomainEventHandlers(assembly);
        services.AddDistributedEventHandlers(assembly);
        services.AddSliceConventions(assembly);   // permission providers + marker services

#if (Postgres)
        // Everything on one Postgres: cache, distributed lock, event bus (LISTEN/NOTIFY), durable jobs,
        // blob storage and pgvector — plus EF data, the outbox/inbox, auth and management.
        var connectionString = context.Configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=app;Username=postgres;Password=postgres";

        services.AddSlicePostgresStack(connectionString);
        services.AddSliceDbContext<AppDbContext>((sp, options) =>
            options.UseSlicePostgres(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<INoteRepository, NoteRepository>();

        // Auth and management are separate DbContexts; EnsureCreated is all-or-nothing per database,
        // so give them their own databases on the same Postgres server (the app data + stack share the
        // main one). Swap to EF migrations to consolidate into a single database with schemas.
        services.AddSliceAuthStore(options => options.UseSlicePostgres(DatabaseSuffixed(connectionString, "_auth")));
        services.AddSliceManagementStore(options => options.UseSlicePostgres(DatabaseSuffixed(connectionString, "_mgmt")));
#else
        services.AddSliceDbContext<AppDbContext>(options =>
            options.UseSqlite(context.Configuration.GetConnectionString("App") ?? "Data Source=app.db"));
        services.AddScoped<INoteRepository, NoteRepository>();

        services.AddSliceAuthStore(options =>
            options.UseSqlite(context.Configuration.GetConnectionString("Auth") ?? "Data Source=auth.db"));

        services.AddSliceManagementStore(options =>
            options.UseSqlite(context.Configuration.GetConnectionString("Management") ?? "Data Source=mgmt.db"));
#endif
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

#if (Postgres)
    private static string DatabaseSuffixed(string connectionString, string suffix)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += suffix;
        return builder.ConnectionString;
    }
#endif
}
