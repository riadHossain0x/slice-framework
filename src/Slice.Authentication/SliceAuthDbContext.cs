using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Slice.EntityFrameworkCore.ExtraProperties;

namespace Slice.Authentication;

/// <summary>
/// Identity + OpenIddict store. Kept as its own bounded context (its own database), separate from
/// feature-module contexts.
/// </summary>
public class SliceAuthDbContext(DbContextOptions<SliceAuthDbContext> options)
    : IdentityDbContext<SliceUser, SliceRole, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);   // bakes in Identity's own global-unique UserNameIndex/RoleNameIndex
        builder.UseOpenIddict();
        builder.ConfigureExtraProperties(Database.ProviderName);   // ExtraProperties on AspNetUsers/AspNetRoles

        // Usernames/role-names are no longer globally unique now that TenantId exists — they're
        // unique per tenant instead. See docs/multitenancy.md ("Tenant-scoped identity") for the
        // full rationale, including why this needs TWO unique indexes per entity, not one: Postgres
        // treats every NULL as distinct for uniqueness purposes, so a single composite
        // (TenantId, NormalizedUserName) index would silently allow unlimited duplicate
        // platform-tier (TenantId IS NULL) usernames on its own — the second, NULL-filtered index
        // closes that gap.
        var filters = TenantIndexFilters.For(Database.ProviderName);

        builder.Entity<SliceUser>(b =>
        {
            // Demote the base class's global-unique index to a plain lookup index — multi-tenant
            // login still needs an efficient "every tenant matching this username" scan. IMPORTANT:
            // this must be called with ONLY the property expression, no explicit index name — the
            // base class's own HasIndex(u => u.NormalizedUserName) call (it only sets a database
            // name via HasDatabaseName("UserNameIndex"), not an explicit index Name) is matched by
            // EF's model builder on property list alone. Passing an explicit name here creates a
            // SECOND, independent index instead of reconfiguring the base one in place, leaving the
            // original global-unique index active underneath — this compiles and builds clean; it
            // only shows up by inspecting the generated DDL, not from `dotnet build`.
            b.HasIndex(u => u.NormalizedUserName).IsUnique(false);

            b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }, "UX_AspNetUsers_TenantId_NormalizedUserName")
                .IsUnique()
                .HasFilter(filters.TenantScoped);

            if (filters.SupportsPartialIndexes)
                b.HasIndex(u => u.NormalizedUserName, "UX_AspNetUsers_NormalizedUserName_PlatformOnly")
                    .IsUnique()
                    .HasFilter(filters.PlatformOnly);

            // Emails follow exactly the same rule as usernames: unique within a tenant, repeatable
            // across tenants. Identity's own global RequireUniqueEmail is switched off in
            // SliceAuthenticationModule, so without these the "once per tenant" guarantee the docs
            // promise would rest on the validator alone, with nothing enforcing it at the database.
            b.HasIndex(u => u.NormalizedEmail).IsUnique(false);

            b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }, "UX_AspNetUsers_TenantId_NormalizedEmail")
                .IsUnique()
                .HasFilter(filters.TenantScopedNotNull("NormalizedEmail"));

            if (filters.SupportsPartialIndexes)
                b.HasIndex(u => u.NormalizedEmail, "UX_AspNetUsers_NormalizedEmail_PlatformOnly")
                    .IsUnique()
                    .HasFilter(filters.PlatformOnlyNotNull("NormalizedEmail"));
        });

        builder.Entity<SliceRole>(b =>
        {
            // Same rule as above: no explicit name, so this reconfigures the base class's own
            // NormalizedName index in place instead of adding a duplicate alongside it.
            b.HasIndex(r => r.NormalizedName).IsUnique(false);

            b.HasIndex(r => new { r.TenantId, r.NormalizedName }, "UX_AspNetRoles_TenantId_NormalizedName")
                .IsUnique()
                .HasFilter(filters.TenantScoped);

            if (filters.SupportsPartialIndexes)
                b.HasIndex(r => r.NormalizedName, "UX_AspNetRoles_NormalizedName_PlatformOnly")
                    .IsUnique()
                    .HasFilter(filters.PlatformOnly);
        });
    }
}

/// <summary>
/// Provider-specific <c>WHERE</c> clauses for the tenant-scoped unique indexes.
///
/// <see cref="RelationalIndexBuilderExtensions.HasFilter"/> takes raw SQL, so the identifier quoting
/// has to match the target database — the same reason <c>ConfigureExtraProperties</c> takes a
/// provider name. ANSI double quotes are right for PostgreSQL and SQLite; SQL Server's idiom is
/// brackets (double quotes work only under <c>SET QUOTED_IDENTIFIER ON</c>, which is not guaranteed
/// for whoever runs the DDL); MySQL/MariaDB has no filtered-index support at all.
///
/// Where partial indexes are unavailable, <see cref="SupportsPartialIndexes"/> is false and only the
/// unfiltered composite index is created. That still enforces uniqueness for in-tenant rows, but NOT
/// for the platform tier (TenantId IS NULL), because SQL treats every NULL as distinct — on those
/// providers platform-tier uniqueness rests on <see cref="TenantScopedUserValidator"/> /
/// <see cref="TenantScopedRoleValidator"/> alone, which is a race-prone application-level check
/// rather than a database guarantee.
/// </summary>
internal sealed record TenantIndexFilters(string Quote, string Unquote, bool SupportsPartialIndexes)
{
    public static TenantIndexFilters For(string? providerName) => providerName switch
    {
        null => Ansi with { SupportsPartialIndexes = false },   // in-memory / no relational provider
        var p when p.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) => new("[", "]", true),
        var p when p.Contains("MySql", StringComparison.OrdinalIgnoreCase)
                || p.Contains("MariaDb", StringComparison.OrdinalIgnoreCase)
                || p.Contains("Oracle", StringComparison.OrdinalIgnoreCase) => Ansi with { SupportsPartialIndexes = false },
        _ => Ansi   // Npgsql, SQLite, and other ANSI-quoting relational providers
    };

    private static readonly TenantIndexFilters Ansi = new("\"", "\"", true);

    private string Column(string name) => $"{Quote}{name}{Unquote}";

    /// <summary>Rows belonging to some tenant.</summary>
    public string? TenantScoped => SupportsPartialIndexes ? $"{Column("TenantId")} IS NOT NULL" : null;

    /// <summary>Platform-tier rows (no tenant).</summary>
    public string? PlatformOnly => SupportsPartialIndexes ? $"{Column("TenantId")} IS NULL" : null;

    /// <summary>
    /// As <see cref="TenantScoped"/>, but also excludes rows where the indexed column is NULL —
    /// needed for the optional email columns so that many users without an email don't collide.
    /// </summary>
    public string? TenantScopedNotNull(string column) =>
        SupportsPartialIndexes ? $"{Column("TenantId")} IS NOT NULL AND {Column(column)} IS NOT NULL" : null;

    /// <summary>As <see cref="PlatformOnly"/>, excluding rows where the indexed column is NULL.</summary>
    public string? PlatformOnlyNotNull(string column) =>
        SupportsPartialIndexes ? $"{Column("TenantId")} IS NULL AND {Column(column)} IS NOT NULL" : null;
}
