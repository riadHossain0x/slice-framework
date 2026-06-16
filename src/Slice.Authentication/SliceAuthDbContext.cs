using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Slice.EntityFrameworkCore.ExtraProperties;

namespace Slice.Authentication;

/// <summary>
/// Identity + OpenIddict store. Kept as its own bounded context (its own database), separate from
/// feature-module contexts.
/// </summary>
public sealed class SliceAuthDbContext(DbContextOptions<SliceAuthDbContext> options)
    : IdentityDbContext<SliceUser, SliceRole, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.ConfigureExtraProperties(Database.ProviderName);   // ExtraProperties on AspNetUsers/AspNetRoles
    }
}
