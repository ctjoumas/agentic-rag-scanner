using System.Text.Json;

namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed class CosmosRbacService(RbacExecutionContext context)
{
    private const string CosmosCustomRoleName = "CosmosDB-DataPlane-FullAccess";
    private const int MaxAttempts = 5;
    private const int RetryDelayMs = 3000;
    private static readonly string[] CosmosCustomRoleDataActions =
    [
        "Microsoft.DocumentDB/databaseAccounts/readMetadata",
        "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/create",
        "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/read",
        "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/delete",
        "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/upsert",
        "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/replace",
        "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/executeQuery",
        "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/write",
        "Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/write",
    ];

    private readonly RbacExecutionContext _context = context;
    private readonly ArmRoleService _armRoles = new ArmRoleService(context);

    public void Configure(string resourceGroup, string accountName, string principalId, string subscriptionId, bool assigneeIsObjectId)
    {
        RbacExecutionContext.PrintSection($"Cosmos DB  [{accountName}]");
        if (!_context.ResourceExists(
                ["az", "cosmosdb", "show", "--name", accountName, "--resource-group", resourceGroup],
                $"Cosmos DB account '{accountName}'"))
        {
            return;
        }

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(["az", "cosmosdb", "show", "--name", accountName, "--resource-group", resourceGroup]);
        string endpoint = Ok && Json.HasValue ? RbacExecutionContext.GetString(Json.Value, "documentEndpoint") ?? string.Empty : string.Empty;
        string cosmosId = Ok && Json.HasValue ? RbacExecutionContext.GetString(Json.Value, "id") ?? string.Empty : string.Empty;

        if (!string.IsNullOrWhiteSpace(cosmosId))
        {
            _armRoles.AssignArmRole("DocumentDB Account Contributor", cosmosId, principalId, accountName, assigneeIsObjectId);
        }

        AssignCosmosDataRole(resourceGroup, accountName, principalId, subscriptionId);

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            Console.WriteLine();
            Console.WriteLine("  Add to .env:");
            Console.WriteLine($"  COSMOS_ENDPOINT={endpoint}");
        }
    }

    private void AssignCosmosDataRole(string resourceGroup, string accountName, string principalId, string subscriptionId)
    {
        string roleId = EnsureCosmosRoleDefinition(resourceGroup, accountName);
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return;
        }

        var (Ok, Json, Message) = RunJsonWithRetry(
            [
                "az",
                "cosmosdb",
                "sql",
                "role",
                "assignment",
                "list",
                "--account-name",
                accountName,
                "--resource-group",
                resourceGroup,
            ]);

        bool roleAlreadyAssigned = false;
        if (Ok && Json.HasValue && Json.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement assignment in Json.Value.EnumerateArray())
            {
                string assignmentPrincipalId = assignment.TryGetProperty("principalId", out JsonElement principalElement)
                    ? principalElement.GetString() ?? string.Empty
                    : string.Empty;

                string roleDefinitionId = assignment.TryGetProperty("roleDefinitionId", out JsonElement roleElement)
                    ? roleElement.GetString() ?? string.Empty
                    : string.Empty;

                if (string.Equals(assignmentPrincipalId, principalId, StringComparison.OrdinalIgnoreCase) &&
                    roleDefinitionId.Contains(roleId, StringComparison.OrdinalIgnoreCase))
                {
                    roleAlreadyAssigned = true;
                    break;
                }
            }
        }

        if (roleAlreadyAssigned)
        {
            RbacExecutionContext.PrintWarning($"'{CosmosCustomRoleName}' already assigned");
            return;
        }

        var create = RunJsonWithRetry(
            [
                "az",
                "cosmosdb",
                "sql",
                "role",
                "assignment",
                "create",
                "--account-name",
                accountName,
                "--resource-group",
                resourceGroup,
                "--scope",
                "/",
                "--principal-id",
                principalId,
                "--role-definition-id",
                roleId,
            ]);

        if (create.Ok)
        {
            RbacExecutionContext.PrintSuccess($"Assigned '{CosmosCustomRoleName}' (data-plane)");
        }
        else if (create.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                 create.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase))
        {
            RbacExecutionContext.PrintWarning($"'{CosmosCustomRoleName}' already assigned");
        }
        else
        {
            RbacExecutionContext.PrintError($"Failed to assign Cosmos DB data role: {create.Message}");
            RbacExecutionContext.PrintDetail("Tip: your account may need 'Owner' or 'User Access Administrator' on the Cosmos account.");
        }
    }

    private string EnsureCosmosRoleDefinition(string resourceGroup, string accountName)
    {
        var (Ok, Json, Message) = RunJsonWithRetry(
            [
                "az",
                "cosmosdb",
                "sql",
                "role",
                "definition",
                "list",
                "--account-name",
                accountName,
                "--resource-group",
                resourceGroup,
            ]);

        if (Ok && Json.HasValue && Json.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement definition in Json.Value.EnumerateArray())
            {
                string roleName = definition.TryGetProperty("roleName", out JsonElement roleNameElement)
                    ? roleNameElement.GetString() ?? string.Empty
                    : string.Empty;

                if (!string.Equals(roleName, CosmosCustomRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string roleId = definition.TryGetProperty("name", out JsonElement roleIdElement)
                    ? roleIdElement.GetString() ?? string.Empty
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(roleId))
                {
                    RbacExecutionContext.PrintWarning($"Cosmos role '{CosmosCustomRoleName}' already exists - reusing");
                    return roleId;
                }
            }
        }

        var roleBody = new
        {
            RoleName = CosmosCustomRoleName,
            Type = "CustomRole",
            AssignableScopes = new[] { "/" },
            Permissions = new[]
            {
                new
                {
                    DataActions = CosmosCustomRoleDataActions,
                },
            },
        };

        string tempFilePath = Path.Combine(Path.GetTempPath(), $"cosmos-role-{Guid.NewGuid():N}.json");
        File.WriteAllText(tempFilePath, JsonSerializer.Serialize(roleBody, new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            var create = RunJsonWithRetry(
                [
                    "az",
                    "cosmosdb",
                    "sql",
                    "role",
                    "definition",
                    "create",
                    "--account-name",
                    accountName,
                    "--resource-group",
                    resourceGroup,
                    "--body",
                    $"@{tempFilePath}",
                ]);

            if (!create.Ok || !create.Json.HasValue)
            {
                if (create.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                    create.Message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase))
                {
                    // Another concurrent run may have created the role; re-query and reuse.
                    return EnsureCosmosRoleDefinition(resourceGroup, accountName);
                }

                RbacExecutionContext.PrintError($"Failed to create Cosmos role definition: {create.Message}");
                return string.Empty;
            }

            string roleId = RbacExecutionContext.GetString(create.Json.Value, "name") ?? string.Empty;
            RbacExecutionContext.PrintSuccess($"Created Cosmos role definition '{CosmosCustomRoleName}' ({roleId})");
            return roleId;
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private static (bool Ok, JsonElement? Json, string Message) RunJsonWithRetry(IReadOnlyList<string> args)
    {
        (bool Ok, JsonElement? Json, string Message) last = (false, null, "Unknown error");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            last = RbacExecutionContext.RunJson(args);
            if (last.Ok)
            {
                return last;
            }

            if (!IsTransient(last.Message) || attempt == MaxAttempts)
            {
                return last;
            }

            Thread.Sleep(RetryDelayMs * attempt);
        }

        return last;
    }

    private static bool IsTransient(string message)
    {
        return message.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("InternalServerError", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }
}

