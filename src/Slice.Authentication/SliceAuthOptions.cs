namespace Slice.Authentication;

/// <summary>Authentication options (bound from the "SliceAuth" configuration section).</summary>
public sealed class SliceAuthOptions
{
    /// <summary>EF connection string for the identity/OpenIddict store.</summary>
    public string ConnectionString { get; set; } = "Data Source=auth.db";

    /// <summary>Seed a demo admin (granted every declared permission) on startup.</summary>
    public bool SeedDemoAdmin { get; set; } = true;

    public string AdminEmail { get; set; } = "admin@slice";
    public string AdminPassword { get; set; } = "Admin123!";
    public string AdminRole { get; set; } = "admin";

    /// <summary>
    /// Register ASP.NET Identity's cookie authentication schemes as well as the bearer/OpenIddict
    /// stack, for a host that server-renders pages instead of (or alongside) serving an API.
    ///
    /// Off by default: <see cref="SliceAuthenticationModule"/> otherwise wires identity with
    /// <c>AddIdentityCore</c> and makes OpenIddict validation the default scheme, which is right for
    /// an API but registers no cookie handler — so <c>SignInManager.SignInAsync</c>, a
    /// <c>[Authorize]</c> Razor Page, and <c>ConfigureApplicationCookie</c> all have nothing to bind
    /// to. Turning this on switches to <c>AddIdentity&lt;SliceUser, SliceRole&gt;</c> (which registers
    /// them) and leaves the default scheme alone so the cookie stays the default for browser
    /// requests; bearer callers select OpenIddict explicitly.
    /// </summary>
    public bool UseIdentityCookies { get; set; }

    /// <summary>
    /// Let <see cref="SliceAuthDataSeeder"/> create the identity schema with
    /// <c>Database.EnsureCreatedAsync()</c> on startup.
    ///
    /// On by default, which suits a fresh database and keeps <c>Slice.Authentication</c>
    /// provider-agnostic (real EF migrations would force it to pick a provider). Turn it off when the
    /// host owns the schema — then <c>EnsureCreated</c>'s central weakness stops mattering: it is a
    /// no-op once the database exists, so on an already-created database it silently never applies a
    /// later schema change. A host that needs versioned, repeatable auth-schema changes should set
    /// this false and give <see cref="SliceAuthDbContext"/> its own migrations in its own assembly
    /// (<c>MigrationsAssembly(...)</c>), starting from a baseline that matches what is already there.
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>
    /// Upper bound on how many candidate accounts the password grant will verify a password against
    /// in one request. Because usernames/emails are only unique per tenant, a login has to check the
    /// submitted password against every row that matches the identifier — and each check is a full
    /// password-hash computation. Without a cap, an address shared by many tenants turns one
    /// unauthenticated request into that many hashes. Past the cap the request is rejected asking for
    /// a <c>tenant_id</c> parameter instead. See <see cref="ConnectController.ResolveUserAsync"/>.
    /// </summary>
    public int MaxLoginCandidates { get; set; } = 10;
}
