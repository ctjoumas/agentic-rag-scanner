namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed class StorageRbacService(RbacExecutionContext context)
{
    private readonly RbacExecutionContext _context = context;
    private readonly ArmRoleService _armRoles = new ArmRoleService(context);

    public void Configure(string resourceGroup, string accountName, string principalId, bool assigneeIsObjectId)
    {
        RbacExecutionContext.PrintSection($"Blob Storage  [{accountName}]");
        if (!_context.ResourceExists(
                ["az", "storage", "account", "show", "--name", accountName, "--resource-group", resourceGroup],
                $"Storage account '{accountName}'"))
        {
            return;
        }

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(["az", "storage", "account", "show", "--name", accountName, "--resource-group", resourceGroup]);
        if (!Ok || !Json.HasValue)
        {
            RbacExecutionContext.PrintError($"Could not retrieve Storage account '{accountName}': {Message}");
            return;
        }

        string saId = RbacExecutionContext.GetString(Json.Value, "id") ?? string.Empty;
        string blobEndpoint = RbacExecutionContext.GetString(Json.Value, "primaryEndpoints", "blob") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(saId))
        {
            RbacExecutionContext.PrintError($"Could not resolve Storage account id for '{accountName}'.");
            return;
        }

        _armRoles.AssignArmRole("Storage Blob Data Contributor", saId, principalId, accountName, assigneeIsObjectId);

        if (!string.IsNullOrWhiteSpace(blobEndpoint))
        {
            Console.WriteLine();
            Console.WriteLine("  Add to .env:");
            Console.WriteLine($"  BLOBSTORAGE_ACCOUNT_URL={blobEndpoint}");
        }
    }
}

