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
            string label = string.IsNullOrWhiteSpace(options.PrincipalName) ? principalId : $"{options.PrincipalName} ({principalId})";
            RbacExecutionContext.PrintSection("Target principal");
            RbacExecutionContext.PrintSuccess($"Granting roles to {options.PrincipalType}: {label}");
        }
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_RBAC_PRINCIPAL_ID")))
        {
            principalId = Environment.GetEnvironmentVariable("AZURE_RBAC_PRINCIPAL_ID")!;
            assigneeIsObjectId = true;
            context.PrincipalType = "User";
            RbacExecutionContext.PrintSection("Target principal");
            RbacExecutionContext.PrintSuccess($"Using principal ID from AZURE_RBAC_PRINCIPAL_ID env var: {principalId}");
        }
        else
        {
            (principalId, assigneeIsObjectId) = GetCurrentUserId(context, options.TenantId);
            context.PrincipalType = "User";
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

        string? foundryProject = options.FoundryProject?.Trim();
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

    private static (string principalId, bool isObjectId) GetCurrentUserId(RbacExecutionContext context, string? tenantId)
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
            RbacExecutionContext.PrintStep("Device code login required to establish a fresh token...");
            RbacExecutionContext.PrintStep("After login, this token will be cached and reused for all RBAC assignments.");
            
            List<string> loginArgs = ["az", "login", "--use-device-code"];
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                loginArgs.Add("--tenant");
                loginArgs.Add(tenantId);
            }

            var loginResult = RbacExecutionContext.RunCommand(loginArgs, capture: false);
            if (loginResult.ExitCode != 0)
            {
                RbacExecutionContext.PrintError("Device code login failed.");
                throw new InvalidOperationException("Device code login failed.");
            }

            // Wait for token cache to refresh
            System.Threading.Thread.Sleep(3000);

            // Retry Graph API call a few times - CAE challenge may take a moment to resolve
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                (ok, uid, err) = TryLookupSignedInUser(context);
                if (ok && !string.IsNullOrWhiteSpace(uid))
                {
                    break;
                }
                
                if (attempt < 3 && IsCaeFailure(err))
                {
                    RbacExecutionContext.PrintWarning($"Graph API still blocked by CAE (attempt {attempt}/3), retrying in 5 seconds...");
                    System.Threading.Thread.Sleep(5000);
                }
            }
        }

        if (!ok || string.IsNullOrWhiteSpace(uid))
        {
            bool caeFailure = IsCaeFailure(err);
            if (caeFailure)
            {
                RbacExecutionContext.PrintWarning("Conditional Access policies prevent Graph API access for object ID lookup.");
                RbacExecutionContext.PrintStep("Using fallback: user principal name (UPN) from cached credentials...");
                RbacExecutionContext.PrintStep("Note: Some RBAC assignments may fail and will need to be configured via Azure Portal.");
                
                // Try to get UPN from cached login without triggering new auth
                var upnResult = RbacExecutionContext.RunCommand(
                    ["az", "account", "show", "--query", "user.name", "-o", "tsv"],
                    capture: true);
                
                if (upnResult.ExitCode == 0)
                {
                    string upn = upnResult.StdOut.Trim();
                    if (!string.IsNullOrWhiteSpace(upn))
                    {
                        RbacExecutionContext.PrintSuccess($"Using cached user principal: {upn}");
                        return (upn, false); // false = not an object ID, it's a UPN
                    }
                }
            }
            
            RbacExecutionContext.PrintError("Could not retrieve user identity.");
            throw new InvalidOperationException("Could not retrieve signed-in user identity.");
        }

        RbacExecutionContext.PrintSuccess($"User object ID: {uid}");
        return (uid, true); // true = it's an object ID (GUID)
    }

    private static (bool Ok, string UserId, string Error) TryLookupSignedInUser(RbacExecutionContext context)
    {
        var result = RbacExecutionContext.RunCommand(["az", "ad", "signed-in-user", "show", "--only-show-errors", "--query", "id", "-o", "tsv"], capture: true);
        return (result.ExitCode == 0, result.StdOut.Trim(), result.StdErr.Trim());
    }
}
