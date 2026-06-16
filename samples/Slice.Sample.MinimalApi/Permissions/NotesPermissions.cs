using Slice.Authorization;

namespace Slice.Sample.MinimalApi.Permissions;

public static class NotesPermissions
{
    public const string GroupName = "Notes";

    public const string View = GroupName + ".View";
    public const string Create = GroupName + ".Create";
}

public sealed class NotesPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(NotesPermissions.GroupName, "Notes");

        // grantedByDefault so the sample runs anonymously (no auth server). A real app would grant these
        // to roles/users via Slice.Management and require an authenticated caller — see the CRM sample.
        var view = group.AddPermission(NotesPermissions.View, "View notes", grantedByDefault: true);
        view.AddChild(NotesPermissions.Create, "Create notes", grantedByDefault: true);
    }
}
