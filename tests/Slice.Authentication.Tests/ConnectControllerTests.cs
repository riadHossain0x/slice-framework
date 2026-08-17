using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Authentication.Tests;

/// <summary>
/// Covers the password-grant logic that tenant-scoped identity changed: resolving a login when the
/// submitted identifier is no longer globally unique, and building a principal whose permissions come
/// only from the signing-in account's own roles.
///
/// Exercised through a subclass rather than over HTTP — <see cref="ConnectController.Exchange"/> needs
/// a live OpenIddict server request, but the two hooks under test never touch <c>HttpContext</c>, and
/// they are already <c>protected virtual</c> as the documented extension seam.
/// </summary>
public sealed class ConnectControllerTests : AuthDbFixture
{
    private const string Password = "Password1!";

    private sealed class TestableConnectController(
        UserManager<SliceUser> users,
        RoleManager<SliceRole> roles,
        ITenantRoleAssigner assigner,
        SliceAuthOptions options)
        : ConnectController(users, roles, assigner, options)
    {
        public Task<LoginCandidates> Resolve(string usernameOrEmail, string password, string? tenantIdHint = null)
            => ResolveUserAsync(usernameOrEmail, password, tenantIdHint);

        public Task<ClaimsPrincipal> Principal(SliceUser user)
            => CreatePrincipalAsync(user, ImmutableArray<string>.Empty);
    }

    private static TestableConnectController ControllerFrom(IServiceProvider sp) => new(
        sp.GetRequiredService<UserManager<SliceUser>>(),
        sp.GetRequiredService<RoleManager<SliceRole>>(),
        sp.GetRequiredService<ITenantRoleAssigner>(),
        sp.GetRequiredService<SliceAuthOptions>());

    [Fact]
    public async Task Resolve_returns_the_one_account_whose_password_matches()
    {
        await using var scope = Services.CreateAsyncScope();
        await CreateUser(scope.ServiceProvider, "solo@test.local", Guid.NewGuid());

        var result = await ControllerFrom(scope.ServiceProvider).Resolve("solo@test.local", Password);

        Assert.False(result.ExceededCandidateLimit);
        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task Resolve_returns_nothing_when_the_password_is_wrong()
    {
        await using var scope = Services.CreateAsyncScope();
        await CreateUser(scope.ServiceProvider, "solo@test.local", Guid.NewGuid());

        var result = await ControllerFrom(scope.ServiceProvider).Resolve("solo@test.local", "WrongPassword1!");

        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Resolve_matches_on_the_email_as_well_as_the_username()
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<SliceUser>>();
        var created = await users.CreateAsync(
            new SliceUser { UserName = "jdoe", Email = "j.doe@test.local", TenantId = Guid.NewGuid() }, Password);
        Assert.True(created.Succeeded);

        var byUserName = await ControllerFrom(scope.ServiceProvider).Resolve("jdoe", Password);
        var byEmail = await ControllerFrom(scope.ServiceProvider).Resolve("j.doe@test.local", Password);

        Assert.Single(byUserName.Matches);
        Assert.Single(byEmail.Matches);
    }

    [Fact]
    public async Task Resolve_returns_both_accounts_when_the_same_credentials_are_valid_in_two_tenants()
    {
        await using var scope = Services.CreateAsyncScope();
        await CreateUser(scope.ServiceProvider, "shared@test.local", Guid.NewGuid());
        await CreateUser(scope.ServiceProvider, "shared@test.local", Guid.NewGuid());

        var result = await ControllerFrom(scope.ServiceProvider).Resolve("shared@test.local", Password);

        // Ambiguous on purpose — Exchange() turns this into the "retry with tenant_id" rejection
        // rather than signing the caller into whichever tenant happened to be returned first.
        Assert.Equal(2, result.Matches.Count);
    }

    [Fact]
    public async Task Resolve_narrows_to_one_account_when_a_tenant_id_hint_is_supplied()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        await CreateUser(scope.ServiceProvider, "shared@test.local", tenantA);
        await CreateUser(scope.ServiceProvider, "shared@test.local", tenantB);

        var result = await ControllerFrom(scope.ServiceProvider).Resolve("shared@test.local", Password, tenantB.ToString());

        Assert.Equal(tenantB, Assert.Single(result.Matches).TenantId);
    }

    [Fact]
    public async Task Resolve_only_matches_a_password_belonging_to_the_hinted_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        await CreateUser(scope.ServiceProvider, "shared@test.local", tenantA);
        await CreateUser(scope.ServiceProvider, "shared@test.local", tenantB, password: "DifferentPassword1!");

        var result = await ControllerFrom(scope.ServiceProvider).Resolve("shared@test.local", Password, tenantB.ToString());

        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Resolve_reports_the_candidate_limit_rather_than_hashing_every_row()
    {
        await using var scope = Services.CreateAsyncScope();
        for (var i = 0; i < 4; i++)
            await CreateUser(scope.ServiceProvider, "popular@test.local", Guid.NewGuid());

        var result = await ControllerFrom(scope.ServiceProvider).Resolve("popular@test.local", Password);

        Assert.True(result.ExceededCandidateLimit);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Principal_carries_a_tenant_id_claim_for_an_in_tenant_user()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        var user = await CreateUser(scope.ServiceProvider, "staff@test.local", tenant);

        var principal = await ControllerFrom(scope.ServiceProvider).Principal(user);

        Assert.Equal(tenant.ToString(), principal.FindFirst(SliceClaims.TenantId)?.Value);
    }

    [Fact]
    public async Task Principal_carries_no_tenant_id_claim_for_a_platform_user()
    {
        await using var scope = Services.CreateAsyncScope();
        var user = await CreateUser(scope.ServiceProvider, "root@test.local", tenantId: null);

        var principal = await ControllerFrom(scope.ServiceProvider).Principal(user);

        // Absent, not empty — Slice.MultiTenancy's ClaimTenantResolveContributor reads this claim, so
        // a platform account has to leave the ambient tenant unresolved rather than set it to nothing.
        Assert.Null(principal.FindFirst(SliceClaims.TenantId));
    }

    [Fact]
    public async Task Principal_carries_only_the_users_own_tenants_permissions_when_role_names_collide()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        await CreateRole(scope.ServiceProvider, "Manager", tenantA, (SliceClaims.Permission, "leads.read"));
        await CreateRole(scope.ServiceProvider, "Manager", tenantB, (SliceClaims.Permission, "billing.write"));

        var user = await CreateUser(scope.ServiceProvider, "staff@test.local", tenantA);
        await scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>().AddToRoleAsync(user, "Manager");

        var principal = await ControllerFrom(scope.ServiceProvider).Principal(user);
        var permissions = principal.FindAll(SliceClaims.Permission).Select(c => c.Value).ToList();

        Assert.Equal(["leads.read"], permissions);
    }

    [Fact]
    public async Task Principal_carries_permission_deny_claims_so_the_store_can_apply_them()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        await CreateRole(scope.ServiceProvider, "Restricted", tenant,
            (SliceClaims.Permission, "leads.read"),
            (SliceClaims.PermissionDeny, "leads.delete"),
            (SliceClaims.DataScope, "own-region"));

        var user = await CreateUser(scope.ServiceProvider, "staff@test.local", tenant);
        await scope.ServiceProvider.GetRequiredService<ITenantRoleAssigner>().AddToRoleAsync(user, "Restricted");

        var principal = await ControllerFrom(scope.ServiceProvider).Principal(user);

        Assert.Equal("leads.read", principal.FindFirst(SliceClaims.Permission)?.Value);
        Assert.Equal("leads.delete", principal.FindFirst(SliceClaims.PermissionDeny)?.Value);
        Assert.Equal("own-region", principal.FindFirst(SliceClaims.DataScope)?.Value);
    }

    [Fact]
    public async Task Principal_keeps_the_permissions_of_a_role_granted_from_outside_the_users_tenant()
    {
        var tenant = Guid.NewGuid();

        await using var scope = Services.CreateAsyncScope();
        // A platform-tier role deliberately granted to in-tenant staff. Re-resolving the user's role
        // names against their own tenant would drop it; resolving via the role links keeps it.
        var support = await CreateRole(scope.ServiceProvider, "Support", tenantId: null,
            (SliceClaims.Permission, "diagnostics.read"));

        var user = await CreateUser(scope.ServiceProvider, "staff@test.local", tenant);
        var db = scope.ServiceProvider.GetRequiredService<SliceAuthDbContext>();
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = support.Id });
        await db.SaveChangesAsync();

        var principal = await ControllerFrom(scope.ServiceProvider).Principal(user);

        Assert.Contains("diagnostics.read", principal.FindAll(SliceClaims.Permission).Select(c => c.Value));
    }

    /// <summary>Three rows is over the cap, so the limit path is reachable without seeding a crowd.</summary>
    protected override void Configure(SliceAuthOptions options) => options.MaxLoginCandidates = 3;

    private static async Task<SliceUser> CreateUser(
        IServiceProvider sp, string email, Guid? tenantId, string password = Password)
    {
        var users = sp.GetRequiredService<UserManager<SliceUser>>();
        var user = new SliceUser { UserName = email, Email = email, TenantId = tenantId };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private static async Task<SliceRole> CreateRole(
        IServiceProvider sp, string name, Guid? tenantId, params (string Type, string Value)[] claims)
    {
        var roles = sp.GetRequiredService<RoleManager<SliceRole>>();
        var role = new SliceRole { Name = name, TenantId = tenantId };
        Assert.True((await roles.CreateAsync(role)).Succeeded);

        foreach (var (type, value) in claims)
            await roles.AddClaimAsync(role, new Claim(type, value));

        return role;
    }
}
