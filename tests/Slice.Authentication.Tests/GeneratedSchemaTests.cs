using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Authentication.Tests;

/// <summary>
/// Asserts against the DDL EF actually emits, not just against observed behaviour. The index-demotion
/// footgun this guards — passing an explicit name to <c>HasIndex</c> creates a SECOND index instead of
/// reconfiguring the base one, leaving Identity's global-unique index quietly in place underneath —
/// compiles clean and is invisible in a build log. It shipped once. The only thing that catches it is
/// reading the generated schema.
/// </summary>
public sealed class GeneratedSchemaTests : AuthDbFixture
{
    private string Script()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>().Database.GenerateCreateScript();
    }

    [Theory]
    [InlineData("UserNameIndex", "AspNetUsers")]
    [InlineData("RoleNameIndex", "AspNetRoles")]
    public void The_base_identity_name_index_is_demoted_to_a_plain_lookup_index(string index, string table)
    {
        var statement = IndexStatement(Script(), index);

        Assert.Contains(table, statement, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE", statement, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("UX_AspNetUsers_TenantId_NormalizedUserName")]
    [InlineData("UX_AspNetUsers_NormalizedUserName_PlatformOnly")]
    [InlineData("UX_AspNetUsers_TenantId_NormalizedEmail")]
    [InlineData("UX_AspNetUsers_NormalizedEmail_PlatformOnly")]
    [InlineData("UX_AspNetRoles_TenantId_NormalizedName")]
    [InlineData("UX_AspNetRoles_NormalizedName_PlatformOnly")]
    public void Each_tenant_scoped_uniqueness_index_is_created_as_unique_and_filtered(string index)
    {
        var statement = IndexStatement(Script(), index);

        Assert.Contains("UNIQUE", statement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", statement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"TenantId\"", statement, StringComparison.Ordinal);   // SQLite/ANSI quoting
    }

    [Fact]
    public void The_platform_tier_indexes_are_the_ones_filtered_to_a_null_tenant()
    {
        var script = Script();

        Assert.Contains("\"TenantId\" IS NULL",
            IndexStatement(script, "UX_AspNetUsers_NormalizedUserName_PlatformOnly"), StringComparison.Ordinal);
        Assert.Contains("\"TenantId\" IS NOT NULL",
            IndexStatement(script, "UX_AspNetUsers_TenantId_NormalizedUserName"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_email_indexes_skip_rows_that_have_no_email()
    {
        // Otherwise every account without an email would collide with every other one in its tenant.
        Assert.Contains("\"NormalizedEmail\" IS NOT NULL",
            IndexStatement(Script(), "UX_AspNetUsers_TenantId_NormalizedEmail"), StringComparison.Ordinal);
    }

    [Fact]
    public void No_unique_index_is_left_on_the_bare_username_or_role_name()
    {
        // The regression this whole file exists for: a leftover global-unique index would keep
        // cross-tenant duplicates blocked while every other signal said they were allowed.
        foreach (var statement in Statements(Script()).Where(s => s.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)))
            Assert.True(
                statement.Contains("WHERE", StringComparison.OrdinalIgnoreCase)
                || statement.Contains("\"TenantId\"", StringComparison.Ordinal)
                || !MentionsIdentityNameColumn(statement),
                $"Unfiltered unique index on a tenant-scoped name column: {statement}");
    }

    private static bool MentionsIdentityNameColumn(string statement) =>
        statement.Contains("\"NormalizedUserName\"", StringComparison.Ordinal)
        || statement.Contains("\"NormalizedName\"", StringComparison.Ordinal)
        || statement.Contains("\"NormalizedEmail\"", StringComparison.Ordinal);

    private static IEnumerable<string> Statements(string script) =>
        script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string IndexStatement(string script, string indexName)
        => Statements(script).SingleOrDefault(s => s.Contains($"\"{indexName}\"", StringComparison.Ordinal))
           ?? throw new Xunit.Sdk.XunitException($"No index named '{indexName}' in the generated schema.");
}
