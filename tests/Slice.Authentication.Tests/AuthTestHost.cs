using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Slice.Authentication.Tests;

/// <summary>
/// Builds a minimal DI container around a file-based SQLite <see cref="SliceAuthDbContext"/> — just
/// Identity core plus the tenant-scoped validators/role-assigner under test, not the full
/// <see cref="SliceAuthenticationModule"/> (which also wires the OpenIddict server/validation and
/// needs signing keys, HTTP context, etc. that these focused tests don't exercise).
/// </summary>
public static class AuthTestHost
{
    public static ServiceProvider Build(
        string connectionString,
        Action<SliceAuthOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var options = new SliceAuthOptions();
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddDbContext<SliceAuthDbContext>(o => o.UseSqlite(connectionString));

        services.AddIdentityCore<SliceUser>(o =>
            {
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = false;
            })
            .AddRoles<SliceRole>()
            .AddEntityFrameworkStores<SliceAuthDbContext>();

        services.RemoveAll<IUserValidator<SliceUser>>();
        services.AddScoped<IUserValidator<SliceUser>, TenantScopedUserValidator>();
        services.RemoveAll<IRoleValidator<SliceRole>>();
        services.AddScoped<IRoleValidator<SliceRole>, TenantScopedRoleValidator>();

        services.AddScoped<ITenantRoleAssigner, TenantRoleAssigner>();
        services.AddScoped<IUserClaimsPrincipalFactory<SliceUser>, TenantSafeUserClaimsPrincipalFactory>();

        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Per-test-class isolation: xunit creates a new instance per [Fact], so each test gets its own
/// temp-directory SQLite file and DI container — same pattern as Slice.Data.Tests/TenantDatabaseTests.
/// </summary>
public abstract class AuthDbFixture : IAsyncLifetime
{
    private readonly string _dirRoot = Path.Combine(Path.GetTempPath(), $"slice-auth-tests-{Guid.NewGuid():N}");

    protected ServiceProvider Services { get; private set; } = null!;

    /// <summary>Override to vary the options a test class runs under.</summary>
    protected virtual void Configure(SliceAuthOptions options) { }

    /// <summary>Override to add services a test class needs on top of the shared identity wiring.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dirRoot);
        Services = AuthTestHost.Build($"Data Source={Path.Combine(_dirRoot, "auth.db")}", Configure, ConfigureServices);

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        Services.Dispose();
        if (Directory.Exists(_dirRoot)) Directory.Delete(_dirRoot, recursive: true);
        return Task.CompletedTask;
    }
}
