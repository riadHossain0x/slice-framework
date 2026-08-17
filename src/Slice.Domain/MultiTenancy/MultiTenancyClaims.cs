namespace Slice.Domain.MultiTenancy;

/// <summary>
/// Claim type carrying the authenticated principal's tenant id. Shared between
/// <c>Slice.Authentication</c> (which issues it) and <c>Slice.MultiTenancy</c> (which reads it via
/// <c>ClaimTenantResolveContributor</c>) so both stay pointed at the same literal without either
/// project depending on the other.
/// </summary>
public static class MultiTenancyClaims
{
    public const string TenantId = "tenant_id";
}
