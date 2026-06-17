using Slice.Authorization;

namespace SliceMinimalApp.Permissions;

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

        // grantedByDefault so the generated app runs anonymously (no auth server wired). A real app would
        // grant these to roles/users via Slice.Management and require an authenticated caller.
        var view = group.AddPermission(NotesPermissions.View, "View notes", grantedByDefault: true);
        view.AddChild(NotesPermissions.Create, "Create notes", grantedByDefault: true);
    }
}
