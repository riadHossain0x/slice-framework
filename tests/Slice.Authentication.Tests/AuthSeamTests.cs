using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Authentication.Tests;

/// <summary>
/// The extension points <c>Slice.Authentication</c> offers a host, as distinct from behaviour it
/// imposes. A host that server-renders pages, or that has its own reasons to refuse an
/// otherwise-valid credential, has to be able to get those without forking the module — these pin
/// down that it can.
/// </summary>
public sealed class AuthSeamTests
{
    // --- SliceAuthOptions defaults: the seams must be off/permissive unless asked for ---

    [Fact]
    public void Identity_cookies_are_off_by_default()
    {
        // The API-first default. Turning this on changes the default authentication scheme, so it
        // must never happen implicitly.
        Assert.False(new SliceAuthOptions().UseIdentityCookies);
    }

    [Fact]
    public void Schema_auto_creation_is_on_by_default()
    {
        // Preserves the pre-existing behaviour for every host that hasn't opted into owning migrations.
        Assert.True(new SliceAuthOptions().AutoCreateSchema);
    }

    // --- ValidateBeforeSignInAsync ---

    private sealed class GateController(
        UserManager<SliceUser> users,
        RoleManager<SliceRole> roles,
        ITenantRoleAssigner assigner,
        SliceAuthOptions options)
        : ConnectController(users, roles, assigner, options)
    {
        public Task<Microsoft.AspNetCore.Mvc.IActionResult?> Gate(SliceUser user) => ValidateBeforeSignInAsync(user);
    }

    [Fact]
    public async Task The_pre_sign_in_gate_allows_by_default()
    {
        // Default must be "allow" — a seam that denied by default would break every existing host.
        var options = new SliceAuthOptions();
        var controller = new GateController(null!, null!, null!, options);

        Assert.Null(await controller.Gate(new SliceUser { UserName = "someone" }));
    }
}

/// <summary>
/// The pre-sign-in gate has to run on the refresh_token grant too, not just the password grant.
/// That is the whole reason it exists as a framework seam: no middleware runs inside the token
/// exchange, so a host cannot enforce "this account/tenant was suspended after the token was
/// issued" anywhere else. A refactor that drops either call site is a silent security regression,
/// so this asserts against the source itself rather than only the default behaviour.
/// </summary>
public sealed class PreSignInGateCallSiteTests
{
    [Fact]
    public void The_gate_is_invoked_on_both_grants()
    {
        var source = ConnectControllerSource();

        var passwordBranch = source.IndexOf("IsPasswordGrantType", StringComparison.Ordinal);
        var refreshBranch = source.IndexOf("IsRefreshTokenGrantType", StringComparison.Ordinal);
        Assert.True(passwordBranch >= 0 && refreshBranch > passwordBranch);

        Assert.Contains("ValidateBeforeSignInAsync",
            source[passwordBranch..refreshBranch], StringComparison.Ordinal);
        Assert.Contains("ValidateBeforeSignInAsync",
            source[refreshBranch..], StringComparison.Ordinal);
    }

    private static string ConnectControllerSource()
    {
        // Walk up from the test binary to the repo root rather than hardcoding a path.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Slice.Authentication")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Slice.Authentication", "ConnectController.cs"));
    }
}
