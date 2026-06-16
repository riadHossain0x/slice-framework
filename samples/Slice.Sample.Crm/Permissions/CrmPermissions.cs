using Slice.Authorization;

namespace Slice.Sample.Crm.Permissions;

public static class CrmPermissions
{
    public const string GroupName = "Crm";

    public static class Leads
    {
        public const string View = GroupName + ".Leads.View";
        public const string Create = GroupName + ".Leads.Create";
        public const string Edit = GroupName + ".Leads.Edit";
        public const string Export = GroupName + ".Leads.Export";
    }
}

public sealed class CrmPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(CrmPermissions.GroupName, "CRM");
        var view = group.AddPermission(CrmPermissions.Leads.View, "View leads");
        view.AddChild(CrmPermissions.Leads.Create, "Create leads");
        view.AddChild(CrmPermissions.Leads.Edit, "Edit leads");
        view.AddChild(CrmPermissions.Leads.Export, "Export leads");
    }
}
