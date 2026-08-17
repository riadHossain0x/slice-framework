using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Slice.Authentication;

/// <summary>
/// Replaces ASP.NET Identity's default IUserValidator&lt;SliceUser&gt; — which checks GLOBAL
/// username/email uniqueness — with one scoped to (TenantId, NormalizedUserName). Two different
/// tenants may legitimately share a username/email; the same tenant may not have two.
///
/// This also covers the email, which Identity's own <c>User.RequireUniqueEmail</c> would otherwise
/// handle. That switch is turned off by <see cref="SliceAuthenticationModule"/> because it only knows
/// how to check globally and would reject legitimate same-email-different-tenant registrations — so
/// dropping it without replacing it here would leave email uniqueness enforced nowhere at all. The
/// matching <c>NormalizedEmail</c> indexes in <see cref="SliceAuthDbContext"/> back this up at the
/// database, where a concurrent pair of registrations can't slip past a read-then-write check — on
/// providers that support partial indexes. Where they don't (MySQL/MariaDB, Oracle — see
/// <c>TenantIndexFilters</c>) the platform tier has no such index, so for <c>TenantId == null</c>
/// accounts these checks are the only thing enforcing uniqueness, and two concurrent registrations
/// can both succeed.
///
/// A host that genuinely wants several accounts per address inside one tenant replaces this the same
/// way the module itself does — <c>RemoveAll&lt;IUserValidator&lt;SliceUser&gt;&gt;()</c> plus its own
/// validator — and overrides <c>OnModelCreating</c> to drop the email indexes.
/// </summary>
public sealed class TenantScopedUserValidator : IUserValidator<SliceUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<SliceUser> manager, SliceUser user)
    {
        var errors = new List<IdentityError>();

        // Everything the stock UserValidator does EXCEPT its two global uniqueness lookups, which are
        // what this class exists to replace. The format rules below are not ours to drop — this
        // validator is registered via RemoveAll, so whatever isn't re-implemented here is simply not
        // enforced anywhere. Errors come from manager.ErrorDescriber so they stay localizable and keep
        // the stock codes; only the duplicate messages are ours, because the stock wording ("is already
        // taken") is misleading once "taken" means "taken within this tenant".
        if (string.IsNullOrWhiteSpace(user.UserName))
            return IdentityResult.Failed(manager.ErrorDescriber.InvalidUserName(user.UserName));

        var allowed = manager.Options.User.AllowedUserNameCharacters;
        if (!string.IsNullOrEmpty(allowed) && user.UserName.Any(c => !allowed.Contains(c)))
            return IdentityResult.Failed(manager.ErrorDescriber.InvalidUserName(user.UserName));

        var normalizedUserName = manager.NormalizeName(user.UserName);
        if (await manager.Users.AnyAsync(u => u.Id != user.Id && u.TenantId == user.TenantId && u.NormalizedUserName == normalizedUserName))
            errors.Add(new IdentityError
            {
                Code = "DuplicateUserName",
                Description = user.TenantId is null
                    ? $"Platform username '{user.UserName}' is already taken."
                    : $"Username '{user.UserName}' is already taken within this tenant."
            });

        // Email is optional in Identity — "no email" must not become a value two accounts collide on.
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            if (!new EmailAddressAttribute().IsValid(user.Email))
                errors.Add(manager.ErrorDescriber.InvalidEmail(user.Email));
            else
            {
                var normalizedEmail = manager.NormalizeEmail(user.Email);
                if (await manager.Users.AnyAsync(u => u.Id != user.Id && u.TenantId == user.TenantId && u.NormalizedEmail == normalizedEmail))
                    errors.Add(new IdentityError
                    {
                        Code = "DuplicateEmail",
                        Description = user.TenantId is null
                            ? $"Platform email '{user.Email}' is already taken."
                            : $"Email '{user.Email}' is already taken within this tenant."
                    });
            }
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }
}

/// <summary>Same idea as <see cref="TenantScopedUserValidator"/>, for role names.</summary>
public sealed class TenantScopedRoleValidator : IRoleValidator<SliceRole>
{
    public async Task<IdentityResult> ValidateAsync(RoleManager<SliceRole> manager, SliceRole role)
    {
        if (string.IsNullOrWhiteSpace(role.Name))
            return IdentityResult.Failed(new IdentityError { Code = "InvalidRoleName", Description = "Role name cannot be empty." });

        var normalized = manager.KeyNormalizer.NormalizeName(role.Name);
        var duplicate = await manager.Roles.AnyAsync(r => r.Id != role.Id && r.TenantId == role.TenantId && r.NormalizedName == normalized);

        return duplicate
            ? IdentityResult.Failed(new IdentityError { Code = "DuplicateRoleName", Description = $"Role name '{role.Name}' already exists for this tenant." })
            : IdentityResult.Success;
    }
}
