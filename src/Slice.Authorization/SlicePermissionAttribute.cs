namespace Slice.Authorization;

/// <summary>
/// Declares a permission required to execute a request. Place on the command/query (or its handler).
/// Enforced in-pipeline by <c>AuthorizationBehavior</c> for all callers (web and background).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class SlicePermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
