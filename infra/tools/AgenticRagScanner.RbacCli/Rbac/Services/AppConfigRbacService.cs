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
        string configId = Ok && Json.HasValue ? RbacExecutionContext.GetString(Json.Value, "id") ?? string.Empty : string.Empty;
        string endpoint = Ok && Json.HasValue ? RbacExecutionContext.GetString(Json.Value, "endpoint") ?? string.Empty : string.Empty;

        _armRoles.AssignArmRole("App Configuration Data Reader", configId, principalId, storeName, assigneeIsObjectId);

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            Console.WriteLine();
            Console.WriteLine("  Add to .env:");
            Console.WriteLine($"  APP_CONFIG_ENDPOINT={endpoint}");
        }
    }
}

