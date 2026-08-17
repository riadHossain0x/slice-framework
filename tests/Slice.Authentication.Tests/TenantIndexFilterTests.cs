namespace Slice.Authentication.Tests;

/// <summary>
/// <see cref="SliceAuthDbContext"/>'s unique-index filters are raw SQL, so identifier quoting has to
/// follow the target provider — the module is otherwise deliberately provider-agnostic (the host picks
/// the provider via <c>AddSliceAuthStore</c>), and only the SQLite path gets exercised by the rest of
/// this suite. These cover the branches no local test database can reach.
/// </summary>
public sealed class TenantIndexFilterTests
{
    [Theory]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    public void Ansi_quoting_providers_get_double_quoted_identifiers(string provider)
    {
        var filters = TenantIndexFilters.For(provider);

        Assert.True(filters.SupportsPartialIndexes);
        Assert.Equal("\"TenantId\" IS NOT NULL", filters.TenantScoped);
        Assert.Equal("\"TenantId\" IS NULL", filters.PlatformOnly);
    }

    [Fact]
    public void Sql_server_gets_bracketed_identifiers()
    {
        // Double quotes only parse as an identifier there under SET QUOTED_IDENTIFIER ON, which isn't
        // guaranteed for whoever runs the DDL — and filtered indexes specifically require it.
        var filters = TenantIndexFilters.For("Microsoft.EntityFrameworkCore.SqlServer");

        Assert.True(filters.SupportsPartialIndexes);
        Assert.Equal("[TenantId] IS NOT NULL", filters.TenantScoped);
        Assert.Equal("[TenantId] IS NULL AND [NormalizedEmail] IS NOT NULL",
            filters.PlatformOnlyNotNull("NormalizedEmail"));
    }

    [Theory]
    [InlineData("Pomelo.EntityFrameworkCore.MySql")]
    [InlineData("MySql.EntityFrameworkCore")]
    [InlineData("Oracle.EntityFrameworkCore")]
    [InlineData(null)]
    public void Providers_without_partial_indexes_emit_no_filter_at_all(string? provider)
    {
        // A filter these can't run would fail at migration time. They fall back to the unfiltered
        // composite index, which still covers in-tenant rows — platform-tier uniqueness then rests on
        // TenantScopedUserValidator alone, which is why SupportsPartialIndexes is worth asserting.
        var filters = TenantIndexFilters.For(provider);

        Assert.False(filters.SupportsPartialIndexes);
        Assert.Null(filters.TenantScoped);
        Assert.Null(filters.PlatformOnly);
        Assert.Null(filters.TenantScopedNotNull("NormalizedEmail"));
    }

    [Fact]
    public void The_email_filters_also_exclude_rows_with_no_email()
    {
        var filters = TenantIndexFilters.For("Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.Equal("\"TenantId\" IS NOT NULL AND \"NormalizedEmail\" IS NOT NULL",
            filters.TenantScopedNotNull("NormalizedEmail"));
    }
}
