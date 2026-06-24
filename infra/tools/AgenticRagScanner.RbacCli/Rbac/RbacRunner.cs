using System.Text.Json;
using AgenticRagScanner.RbacCli.Cli;
using AgenticRagScanner.RbacCli.Rbac.Services;

namespace AgenticRagScanner.RbacCli.Rbac;

internal sealed class RbacRunner
{
    public static int Run(RbacOptions options)
    {
        RbacExecutionContext context = new();

        RbacExecutionContext.PrintHeader("RBAC Configuration - Local Dev");

        CheckPrereqs(context);
        EnsureLogin(context, options.TenantId);
        string subscriptionId = SelectSubscription(context, options.Subscription);

        string principalId;
        bool assigneeIsObjectId;

        if (!string.IsNullOrWhiteSpace(options.PrincipalId))
        {
            principalId = options.PrincipalId;
            assigneeIsObjectId = true;
            context.PrincipalType = options.PrincipalType;
            RbacExecutionContext.PrintSection("Target principal");
            RbacExecutionContext.PrintSuccess($"Granting roles to {options.PrincipalType}: {principalId}");
        }
        else
        {
            principalId = GetCurrentUserId(context, options.TenantId);
            assigneeIsObjectId = false;
        }

        RbacExecutionContext.PrintSection("Resources to configure");
        Console.WriteLine("  Leave blank to skip a service.");
        Console.WriteLine();

        string? resourceGroup = PromptIfMissing(options.ResourceGroup, "Resource group name");
        if (string.IsNullOrWhiteSpace(resourceGroup))
        {
            RbacExecutionContext.PrintWarning("No resource group provided - nothing to configure.");
            return 0;
        }

        string? cosmosAccount = PromptIfMissing(options.CosmosAccount, "Cosmos DB account name");
        string? storageAccount = PromptIfMissing(options.StorageAccount, "Storage account name");
        string? foundryAccount = PromptIfMissing(options.AiServicesAccount, "Microsoft Foundry account name");
        string? appConfigStore = PromptIfMissing(options.AppConfigStore, "App Configuration store name");
        string? keyVault = PromptIfMissing(options.KeyVault, "Key Vault name");
        string? appInsights = PromptIfMissing(options.AppInsights, "Application Insights component name");

        CosmosRbacService cosmosRbac = new(context);
        StorageRbacService storageRbac = new(context);
        FoundryRbacService foundryRbac = new(context);
        FoundryProjectIdentityRbacService foundryProjectIdentityRbac = new(context);
        AppConfigRbacService appConfigRbac = new(context);
        KeyVaultRbacService keyVaultRbac = new(context);
        AppInsightsRbacService appInsightsRbac = new(context);

        if (!string.IsNullOrWhiteSpace(cosmosAccount))
        {
            cosmosRbac.Configure(resourceGroup, cosmosAccount, principalId, subscriptionId, assigneeIsObjectId);
        }
        else
        {
            RbacExecutionContext.PrintSkip("Cosmos DB");
        }

        if (!string.IsNullOrWhiteSpace(storageAccount))
        {
            storageRbac.Configure(resourceGroup, storageAccount, principalId, assigneeIsObjectId);
        }
        else
        {
            RbacExecutionContext.PrintSkip("Blob Storage");
        }

        if (!string.IsNullOrWhiteSpace(foundryAccount))
        {
            foundryRbac.Configure(resourceGroup, foundryAccount, principalId, assigneeIsObjectId);
        }
        else
        {
            RbacExecutionContext.PrintSkip("Microsoft Foundry");
        }

        string? foundryProject = PromptIfMissing(options.FoundryProject, "Foundry project name (for KB MCP runtime roles)");
        if (!string.IsNullOrWhiteSpace(foundryProject))
        {
            foundryProjectIdentityRbac.Configure(resourceGroup, foundryProject, foundryAccount, subscriptionId);
        }
        else
        {
            RbacExecutionContext.PrintSkip("Foundry Project Managed Identity");
        }

        if (!string.IsNullOrWhiteSpace(appConfigStore))
        {
            appConfigRbac.Configure(resourceGroup, appConfigStore, principalId, assigneeIsObjectId);
        }
        else
        {
            RbacExecutionContext.PrintSkip("App Configuration");
        }

        if (!string.IsNullOrWhiteSpace(keyVault))
        {
            keyVaultRbac.Configure(resourceGroup, keyVault, principalId, assigneeIsObjectId);
        }
        else
        {
            RbacExecutionContext.PrintSkip("Key Vault");
        }

        if (!string.IsNullOrWhiteSpace(appInsights))
        {
            appInsightsRbac.Configure(resourceGroup, appInsights, principalId, assigneeIsObjectId);
        }
        else
        {
            RbacExecutionContext.PrintSkip("Application Insights");
        }

        PrintSummary(context);
        return 0;
    }

    private static string? PromptIfMissing(string? current, string prompt)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            return current;
        }

        Console.Write($"  {prompt} (leave blank to skip): ");
        return Console.ReadLine()?.Trim();
    }

    private static void PrintSummary(RbacExecutionContext context)
    {
        RbacExecutionContext.PrintHeader("RBAC configuration complete");
        Console.WriteLine();
        Console.WriteLine("  Role propagation can take up to 5 minutes.");
        Console.WriteLine();
        Console.WriteLine("  Next steps:");
        Console.WriteLine("    1. Fill in the endpoint values printed above");
        Console.WriteLine("    2. (Optional) Store secrets in Key Vault and add Key Vault references in App Configuration");
        Console.WriteLine("    3. In VS Code, run the following tasks in order:");
        Console.WriteLine("         > restore");
        Console.WriteLine("         > build");
        Console.WriteLine("         > run-api");
        Console.WriteLine();
        Console.WriteLine("  Use 'az login' again at any time to refresh credentials.");
        Console.WriteLine();
    }

    private static void CheckPrereqs(RbacExecutionContext context)
    {
        RbacExecutionContext.PrintSection("Prerequisites");
        var result = RbacExecutionContext.RunCommand(["az", "--version"], capture: true);
        if (result.ExitCode != 0)
        {
            RbacExecutionContext.PrintError("Azure CLI is not installed. Install from https://aka.ms/azure-cli");
            throw new InvalidOperationException("Azure CLI not found.");
        }

        RbacExecutionContext.PrintSuccess("Azure CLI found");
    }

    private static void EnsureLogin(RbacExecutionContext context, string? tenantId)
    {
        RbacExecutionContext.PrintSection("Authentication");

        var accountResult = RbacExecutionContext.RunJson(["az", "account", "show"]);
        if (accountResult.Ok && accountResult.Json.HasValue)
        {
            string user = RbacExecutionContext.GetString(accountResult.Json.Value, "user", "name") ?? "<unknown>";
            RbacExecutionContext.PrintSuccess($"Already signed in as: {user}");
            return;
        }

        RbacExecutionContext.PrintStep("Not signed in - launching az login...");
        List<string> loginArgs = ["az", "login"];
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            loginArgs.Add("--tenant");
            loginArgs.Add(tenantId);
        }

        var loginResult = RbacExecutionContext.RunCommand(loginArgs, capture: false);
        if (loginResult.ExitCode != 0)
        {
            RbacExecutionContext.PrintError("Login failed.");
            throw new InvalidOperationException("Login failed.");
        }

        accountResult = RbacExecutionContext.RunJson(["az", "account", "show"]);
        if (!accountResult.Ok || !accountResult.Json.HasValue)
        {
            RbacExecutionContext.PrintError("Could not retrieve account after login.");
            throw new InvalidOperationException("Could not retrieve account after login.");
        }

        string signedInUser = RbacExecutionContext.GetString(accountResult.Json.Value, "user", "name") ?? "<unknown>";
        RbacExecutionContext.PrintSuccess($"Signed in as: {signedInUser}");
    }

    private static string SelectSubscription(RbacExecutionContext context, string? subscriptionArg)
    {
        RbacExecutionContext.PrintSection("Subscription");

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(["az", "account", "list", "--query", "[].{id:id, name:name, state:state}", "-o", "json"]);
        if (!Ok || !Json.HasValue || Json.Value.ValueKind != JsonValueKind.Array)
        {
            RbacExecutionContext.PrintError("Could not list subscriptions.");
            throw new InvalidOperationException("Could not list subscriptions.");
        }

        List<(string Id, string Name)> activeSubs = [];
        foreach (JsonElement element in Json.Value.EnumerateArray())
        {
            string? state = element.TryGetProperty("state", out JsonElement stateElement) ? stateElement.GetString() : null;
            if (!string.Equals(state, "Enabled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? id = element.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() : null;
            string? name = element.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
            {
                activeSubs.Add((id, name));
            }
        }

        if (activeSubs.Count == 0)
        {
            RbacExecutionContext.PrintError("No enabled subscriptions found.");
            throw new InvalidOperationException("No enabled subscriptions found.");
        }

        if (!string.IsNullOrWhiteSpace(subscriptionArg))
        {
            var (Id, Name) = activeSubs.FirstOrDefault(s =>
                string.Equals(s.Id, subscriptionArg, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, subscriptionArg, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(Id))
            {
                RbacExecutionContext.PrintError($"Subscription '{subscriptionArg}' not found or not enabled.");
                throw new InvalidOperationException("Subscription not found or not enabled.");
            }

            SetSubscription(context, Id);
            RbacExecutionContext.PrintSuccess($"Using subscription: {Name} ({Id})");
            return Id;
        }

        if (activeSubs.Count == 1)
        {
            var (Id, Name) = activeSubs[0];
            SetSubscription(context, Id);
            RbacExecutionContext.PrintSuccess($"Using subscription: {Name} ({Id})");
            return Id;
        }

        Console.WriteLine();
        Console.WriteLine("  Available subscriptions:");
        for (int index = 0; index < activeSubs.Count; index++)
        {
            var (Id, Name) = activeSubs[index];
            Console.WriteLine($"    [{index + 1}] {Name}");
            RbacExecutionContext.PrintDetail(Id);
        }

        while (true)
        {
            Console.Write("\n  Select subscription number: ");
            string? input = Console.ReadLine();
            if (int.TryParse(input, out int selectedIndex) && selectedIndex > 0 && selectedIndex <= activeSubs.Count)
            {
                var (Id, Name) = activeSubs[selectedIndex - 1];
                SetSubscription(context, Id);
                RbacExecutionContext.PrintSuccess($"Using subscription: {Name} ({Id})");
                return Id;
            }

            RbacExecutionContext.PrintWarning("Invalid selection, try again.");
        }
    }

    private static void SetSubscription(RbacExecutionContext context, string subId)
    {
        var result = RbacExecutionContext.RunCommand(["az", "account", "set", "--subscription", subId], capture: true);
        if (result.ExitCode != 0)
        {
            RbacExecutionContext.PrintError($"Failed to set subscription {subId}");
            throw new InvalidOperationException("Failed to set subscription.");
        }
    }

    private static string GetCurrentUserId(RbacExecutionContext context, string? tenantId)
    {
        RbacExecutionContext.PrintSection("Signed-in user identity");

        static bool IsCaeFailure(string text)
        {
            string[] markers =
            [
                "InteractionRequired",
                "Continuous access evaluation",
                "TokenCreatedWithOutdatedPolicies",
                "AADSTS50173",
                "AADSTS700082",
            ];
            return markers.Any(text.Contains);
        }

        (bool ok, string uid, string err) = TryLookupSignedInUser(context);
        if (!ok && IsCaeFailure(err))
        {
            RbacExecutionContext.PrintStep("Token rejected by Conditional Access - refreshing credentials via interactive login...");
            List<string> loginArgs = ["az", "login"];
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                loginArgs.Add("--tenant");
                loginArgs.Add(tenantId);
            }

            var loginResult = RbacExecutionContext.RunCommand(loginArgs, capture: false);
            if (loginResult.ExitCode != 0)
            {
                RbacExecutionContext.PrintError("Interactive login failed.");
                throw new InvalidOperationException("Interactive login failed.");
            }

            (ok, uid, err) = TryLookupSignedInUser(context);
        }

        if (!ok || string.IsNullOrWhiteSpace(uid))
        {
            if (!string.IsNullOrWhiteSpace(err))
            {
                RbacExecutionContext.PrintDetail(err);
            }

            RbacExecutionContext.PrintError("Could not retrieve signed-in user object ID. Are you logged in as a user (not a service principal)?");
            throw new InvalidOperationException("Could not retrieve signed-in user object ID.");
        }

        RbacExecutionContext.PrintSuccess($"User object ID: {uid}");
        return uid;
    }

    private static (bool Ok, string UserId, string Error) TryLookupSignedInUser(RbacExecutionContext context)
    {
        var result = RbacExecutionContext.RunCommand(["az", "ad", "signed-in-user", "show", "--query", "id", "-o", "tsv"], capture: true);
        return (result.ExitCode == 0, result.StdOut.Trim(), result.StdErr.Trim());
    }
}
