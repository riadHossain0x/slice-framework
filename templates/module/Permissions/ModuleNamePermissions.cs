using Slice.Authorization;

namespace ModuleName.Permissions;

public static class ModuleNamePermissions
{
    public const string GroupName = "ModuleName";

    public static class Items
    {
        public const string View = GroupName + ".Items.View";
        public const string Create = GroupName + ".Items.Create";
    }
}

public sealed class ModuleNamePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(ModuleNamePermissions.GroupName, "ModuleName");
        var view = group.AddPermission(ModuleNamePermissions.Items.View, "View items");
        view.AddChild(ModuleNamePermissions.Items.Create, "Create items");
    }
}
