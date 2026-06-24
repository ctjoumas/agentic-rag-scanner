namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed class FoundryRbacService(RbacExecutionContext context)
{
    private const string FoundryUserRoleId = "53ca6127-db72-4b80-b1b0-d745d6d5456d";

    private readonly RbacExecutionContext _context = context;
    private readonly ArmRoleService _armRoles = new ArmRoleService(context);

    public void Configure(string resourceGroup, string accountName, string principalId, bool assigneeIsObjectId)
    {
        RbacExecutionContext.PrintSection($"Microsoft Foundry  [{accountName}]");
        if (!_context.ResourceExists(
                ["az", "cognitiveservices", "account", "show", "--name", accountName, "--resource-group", resourceGroup],
                $"Microsoft Foundry account '{accountName}'"))
        {
            return;
        }

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(["az", "cognitiveservices", "account", "show", "--name", accountName, "--resource-group", resourceGroup]);
        string accountId = Ok && Json.HasValue ? RbacExecutionContext.GetString(Json.Value, "id") ?? string.Empty : string.Empty;

        _armRoles.AssignArmRole("Azure AI Developer", accountId, principalId, accountName, assigneeIsObjectId);
        _armRoles.AssignArmRole(FoundryUserRoleId, accountId, principalId, accountName, assigneeIsObjectId);
        _armRoles.AssignArmRole("Cognitive Services OpenAI User", accountId, principalId, accountName, assigneeIsObjectId);

        string endpoint = Ok && Json.HasValue ? RbacExecutionContext.GetString(Json.Value, "properties", "endpoint") ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            Console.WriteLine();
            Console.WriteLine("  Add to .env:");
            Console.WriteLine($"  FOUNDRY_PROJECT_ENDPOINT={endpoint}");
        }
    }
}

