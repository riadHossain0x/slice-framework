using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Validation.AspNetCore;
using Slice.Authorization;
using Slice.Modularity;

namespace Slice.Authentication;

/// <summary>
/// Authentication module: ASP.NET Identity (Guid-keyed) + an OpenIddict OAuth2/OIDC server
/// (token endpoint, password/refresh grants, JWT access tokens) + OpenIddict validation for the
/// resource side. Replaces the Core null current-user and the P7 config permission store with the
/// HTTP/claims-backed implementations.
/// </summary>
[DependsOn(typeof(SliceAuthorizationModule))]
public sealed class SliceAuthenticationModule : SliceModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var options = new SliceAuthOptions();
        context.Configuration.GetSection("SliceAuth").Bind(options);
        services.AddSingleton(options);

        services.AddHttpContextAccessor();

        // NOTE: the SliceAuthDbContext (Identity + OpenIddict store) is registered by the host with
        // its chosen EF provider — see AddSliceAuthStore — so the framework stays provider-agnostic.

        // Uniqueness is now per-tenant, not global — enforced by TenantScopedUserValidator below plus
        // the composite DB indexes in SliceAuthDbContext, not RequireUniqueEmail (which only knows how
        // to do a global check and would otherwise reject legitimate same-email-different-tenant
        // registrations).
        void ConfigureIdentity(IdentityOptions o)
        {
            o.Password.RequireNonAlphanumeric = false;
            o.User.RequireUniqueEmail = false;
        }

        if (options.UseIdentityCookies)
        {
            // AddIdentity also registers the Identity cookie schemes and makes the application cookie
            // the default — which is what a server-rendered host needs, so we leave DefaultScheme
            // alone here rather than pointing it at OpenIddict. Bearer callers still work: OpenIddict
            // validation is registered below and selected explicitly by scheme.
            services.AddIdentity<SliceUser, SliceRole>(ConfigureIdentity)
                .AddEntityFrameworkStores<SliceAuthDbContext>()
                .AddDefaultTokenProviders();
        }
        else
        {
            services.AddIdentityCore<SliceUser>(ConfigureIdentity)
                .AddRoles<SliceRole>()
                .AddEntityFrameworkStores<SliceAuthDbContext>()
                .AddSignInManager();
        }

        // AddIdentityCore(...)/AddRoles(...) above register the default validators via
        // TryAddScoped, which only no-ops if something is ALREADY registered — it does not prevent
        // a later registration from adding a second one. UserManager/RoleManager run every
        // registered IUserValidator/IRoleValidator, so without an explicit RemoveAll first, the
        // stock global-uniqueness validators would keep running alongside these tenant-scoped ones.
        services.RemoveAll<IUserValidator<SliceUser>>();
        services.AddScoped<IUserValidator<SliceUser>, TenantScopedUserValidator>();
        services.RemoveAll<IRoleValidator<SliceRole>>();
        services.AddScoped<IRoleValidator<SliceRole>, TenantScopedRoleValidator>();

        // AddRoles/AddIdentity above installed the role-AWARE claims factory, which expands role
        // names through an unfiltered RoleManager.FindByNameAsync and so can merge another tenant's
        // claims into the principal. Replace it — .AddSignInManager() makes that the default path for
        // cookie sign-in, so leaving it would make the framework's own default configuration the
        // leaky one. No RemoveAll needed here, unlike the validators above: nothing resolves this as
        // an IEnumerable, so the single-instance rule applies and the last registration simply wins.
        services.AddScoped<IUserClaimsPrincipalFactory<SliceUser>, TenantSafeUserClaimsPrincipalFactory>();

        // Persist the data-protection keyring in the same store, so auth cookies and any other
        // protected payloads survive restarts and stay valid across every replica of the app rather
        // than each one generating its own keys into a local folder.
        services.AddDataProtection().PersistKeysToDbContext<SliceAuthDbContext>();

        // With cookies on, AddIdentity already chose the application cookie as the default scheme and
        // overriding it here would break every browser request; bearer callers name the OpenIddict
        // scheme explicitly instead.
        if (options.UseIdentityCookies)
            services.AddAuthentication();
        else
            services.AddAuthentication(o =>
                o.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        services.AddAuthorization();

        services.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<SliceAuthDbContext>())
            .AddServer(o =>
            {
                o.SetTokenEndpointUris("connect/token");
                o.AllowPasswordFlow().AllowRefreshTokenFlow();
                o.AcceptAnonymousClients();                 // first-party public client (no secret)
                o.RegisterScopes("api", "offline_access");
                // Ephemeral in-memory keys (no X.509/keychain access — safe headless/dev).
                // Production should register real signing/encryption certificates instead.
                o.AddEphemeralEncryptionKey();
                o.AddEphemeralSigningKey();
                o.DisableAccessTokenEncryption();           // issue plain JWT access tokens
                o.UseAspNetCore()
                    .EnableTokenEndpointPassthrough()
                    .DisableTransportSecurityRequirement(); // dev/HTTP; production should require HTTPS
            })
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseAspNetCore();
            });

        // Replace Core/P7 defaults (registered earlier → these win).
        services.AddSliceConventions(typeof(SliceAuthenticationModule).Assembly);
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        using var scope = context.ServiceProvider.CreateScope();
        await SliceAuthDataSeeder.SeedAsync(scope.ServiceProvider);
    }
}

public static class SliceAuthStoreRegistration
{
    /// <summary>
    /// Registers the identity/OpenIddict <see cref="SliceAuthDbContext"/> with the host's chosen EF
    /// provider (keeps the framework provider-agnostic). Call from the host module.
    /// </summary>
    public static IServiceCollection AddSliceAuthStore(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<SliceAuthDbContext>(b =>
        {
            configure(b);
            b.UseOpenIddict();
        });
        return services;
    }
}

public static class AuthApplicationBuilderExtensions
{
    /// <summary>Adds authentication + authorization middleware (call before MapControllers).</summary>
    public static IApplicationBuilder UseSliceAuthentication(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
