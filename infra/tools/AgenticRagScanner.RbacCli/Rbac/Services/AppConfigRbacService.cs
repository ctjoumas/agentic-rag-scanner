namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed class AppConfigRbacService(RbacExecutionContext context)
{
    private readonly RbacExecutionContext _context = context;
    private readonly ArmRoleService _armRoles = new ArmRoleService(context);

    public void Configure(string resourceGroup, string storeName, string principalId, bool assigneeIsObjectId)
    {
        RbacExecutionContext.PrintSection($"App Configuration  [{storeName}]");
        if (!_context.ResourceExists(
                ["az", "appconfig", "show", "--name", storeName, "--resource-group", resourceGroup],
                $"App Configuration store '{storeName}'"))
        {
            return;
        }

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(["az", "appconfig", "show", "--name", storeName, "--resource-group", resourceGroup]);
        if (!Ok || !Json.HasValue)
        {
            RbacExecutionContext.PrintError($"Could not retrieve App Configuration store '{storeName}': {Message}");
            return;
        }

        string configId = RbacExecutionContext.GetString(Json.Value, "id") ?? string.Empty;
        string endpoint = RbacExecutionContext.GetString(Json.Value, "endpoint") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(configId))
        {
            RbacExecutionContext.PrintError($"Could not resolve App Configuration store resource ID for '{storeName}'.");
            return;
        }

        _armRoles.AssignArmRole("App Configuration Data Reader", configId, principalId, storeName, assigneeIsObjectId);

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            Console.WriteLine();
            Console.WriteLine("  Add to .env:");
            Console.WriteLine($"  APP_CONFIG_ENDPOINT={endpoint}");
        }
    }
}

