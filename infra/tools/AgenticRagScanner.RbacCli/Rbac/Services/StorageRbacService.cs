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
        string saId = Ok && Json.HasValue ? RbacExecutionContext.GetString(Json.Value, "id") ?? string.Empty : string.Empty;
        string blobEndpoint = Ok && Json.HasValue ? RbacExecutionContext.GetString(Json.Value, "primaryEndpoints", "blob") ?? string.Empty : string.Empty;

        _armRoles.AssignArmRole("Storage Blob Data Contributor", saId, principalId, accountName, assigneeIsObjectId);

        if (!string.IsNullOrWhiteSpace(blobEndpoint))
        {
            Console.WriteLine();
            Console.WriteLine("  Add to .env:");
            Console.WriteLine($"  BLOBSTORAGE_ACCOUNT_URL={blobEndpoint}");
        }
    }
}

