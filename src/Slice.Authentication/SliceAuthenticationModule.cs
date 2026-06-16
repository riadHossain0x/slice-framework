using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddIdentityCore<SliceUser>(o =>
            {
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<SliceRole>()
            .AddEntityFrameworkStores<SliceAuthDbContext>()
            .AddSignInManager();

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
