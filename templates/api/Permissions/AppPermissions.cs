using Slice.Authorization;

namespace SliceApp.Permissions;

public static class AppPermissions
{
    public const string GroupName = "App";

    public static class Notes
    {
        public const string View = GroupName + ".Notes.View";
        public const string Create = GroupName + ".Notes.Create";
    }
}

public sealed class AppPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(AppPermissions.GroupName, "App");
        var view = group.AddPermission(AppPermissions.Notes.View, "View notes");
        view.AddChild(AppPermissions.Notes.Create, "Create notes");
    }
}
