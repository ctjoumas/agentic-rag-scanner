namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed class AppInsightsRbacService(RbacExecutionContext context)
{
    private readonly RbacExecutionContext _context = context;
    private readonly ArmRoleService _armRoles = new ArmRoleService(context);

    public void Configure(string resourceGroup, string componentName, string principalId, bool assigneeIsObjectId)
    {
        RbacExecutionContext.PrintSection($"Application Insights  [{componentName}]");
        if (!_context.ResourceExists(
                ["az", "monitor", "app-insights", "component", "show", "--app", componentName, "--resource-group", resourceGroup],
                $"App Insights component '{componentName}'"))
        {
            return;
        }

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(
            ["az", "monitor", "app-insights", "component", "show", "--app", componentName, "--resource-group", resourceGroup]);
        if (!Ok || !Json.HasValue)
        {
            RbacExecutionContext.PrintError($"Could not retrieve App Insights component '{componentName}'.");
            return;
        }

        string appInsightsId = RbacExecutionContext.GetString(Json.Value, "id") ?? string.Empty;
        _armRoles.AssignArmRole("Log Analytics Reader", appInsightsId, principalId, componentName, assigneeIsObjectId);

        string workspaceId = RbacExecutionContext.GetString(Json.Value, "workspaceResourceId") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            _armRoles.AssignArmRole("Log Analytics Reader", workspaceId, principalId, $"{componentName} (workspace)", assigneeIsObjectId);
        }
    }
}

