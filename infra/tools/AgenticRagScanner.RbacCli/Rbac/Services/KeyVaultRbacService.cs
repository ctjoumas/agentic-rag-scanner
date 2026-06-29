namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed class KeyVaultRbacService(RbacExecutionContext context)
{
    private readonly RbacExecutionContext _context = context;
    private readonly ArmRoleService _armRoles = new ArmRoleService(context);

    public void Configure(string resourceGroup, string vaultName, string principalId, bool assigneeIsObjectId)
    {
        RbacExecutionContext.PrintSection($"Key Vault  [{vaultName}]");
        if (!_context.ResourceExists(
                ["az", "keyvault", "show", "--name", vaultName, "--resource-group", resourceGroup],
                $"Key Vault '{vaultName}'"))
        {
            return;
        }

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(["az", "keyvault", "show", "--name", vaultName, "--resource-group", resourceGroup]);
        if (!Ok || !Json.HasValue)
        {
            RbacExecutionContext.PrintError($"Could not retrieve Key Vault '{vaultName}'.");
            return;
        }

        string vaultId = RbacExecutionContext.GetString(Json.Value, "id") ?? string.Empty;
        string vaultUri = RbacExecutionContext.GetString(Json.Value, "properties", "vaultUri") ?? string.Empty;
        bool rbacEnabled = RbacExecutionContext.GetBool(Json.Value, "properties", "enableRbacAuthorization");

        if (!rbacEnabled)
        {
            RbacExecutionContext.PrintWarning($"Key Vault '{vaultName}' does not have RBAC authorization enabled. Enable it with:");
            RbacExecutionContext.PrintDetail($"az keyvault update --name {vaultName} --resource-group {resourceGroup} --enable-rbac-authorization true");
            return;
        }

        _armRoles.AssignArmRole("Key Vault Secrets User", vaultId, principalId, vaultName, assigneeIsObjectId);

        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            Console.WriteLine();
            Console.WriteLine("  Store secrets here and reference them from App Configuration:");
            Console.WriteLine($"    Vault URI: {vaultUri}");
            Console.WriteLine("  In the App Configuration store, add a 'Key Vault reference' value");
            Console.WriteLine("  pointing to each secret. The SDK resolves them automatically at startup.");
        }
    }
}

