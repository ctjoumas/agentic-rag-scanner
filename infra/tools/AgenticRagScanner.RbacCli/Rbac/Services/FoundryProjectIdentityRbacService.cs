namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed class FoundryProjectIdentityRbacService(RbacExecutionContext context)
{
    private readonly RbacExecutionContext _context = context;
    private readonly ArmRoleService _armRoles = new ArmRoleService(context);

    public void Configure(string resourceGroup, string projectName, string? foundryAccountName, string subscriptionId)
    {
        RbacExecutionContext.PrintSection($"Foundry Project Managed Identity  [{projectName}]");

        if (string.IsNullOrWhiteSpace(foundryAccountName))
        {
            RbacExecutionContext.PrintError("Cannot resolve Foundry project identity: --foundry-account is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            RbacExecutionContext.PrintError("Cannot resolve Foundry project identity: subscription_id is required.");
            return;
        }

        string projectResourceId =
            $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.CognitiveServices/accounts/{foundryAccountName}/projects/{projectName}";

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(["az", "resource", "show", "--ids", projectResourceId]);
        if (!Ok || !Json.HasValue)
        {
            RbacExecutionContext.PrintError($"Foundry project '{projectName}' not found (resource ID: {projectResourceId}).");
            return;
        }

        string projectPrincipalId = RbacExecutionContext.GetString(Json.Value, "identity", "principalId") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(projectPrincipalId))
        {
            RbacExecutionContext.PrintError($"Could not retrieve managed identity principal ID for project '{projectName}'. Ensure the project has a system-assigned managed identity enabled.");
            return;
        }

        RbacExecutionContext.PrintSuccess($"Foundry project identity principal ID: {projectPrincipalId}");

        var foundryResult = RbacExecutionContext.RunJson(["az", "cognitiveservices", "account", "show", "--name", foundryAccountName, "--resource-group", resourceGroup]);
        string foundryId = foundryResult.Ok && foundryResult.Json.HasValue ? RbacExecutionContext.GetString(foundryResult.Json.Value, "id") ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(foundryId))
        {
            _armRoles.AssignArmRole("Cognitive Services Data Contributor (Preview)", foundryId, projectPrincipalId, $"{foundryAccountName} (Foundry project MI)", assigneeIsObjectId: true);
            _armRoles.AssignArmRole("Search Service Contributor", foundryId, projectPrincipalId, $"{foundryAccountName} (Foundry project MI)", assigneeIsObjectId: true);
        }
        else
        {
            RbacExecutionContext.PrintWarning($"Microsoft Foundry account '{foundryAccountName}' not found - skipping Foundry roles.");
        }
    }
}

