using System.CommandLine;
using System.CommandLine.Invocation;
using AgenticRagScanner.RbacCli.Rbac;

namespace AgenticRagScanner.RbacCli.Cli;

internal static class CliParser
{
    public static int Invoke(string[] args)
    {
        return BuildRootCommand().Invoke(args);
    }

    private static RootCommand BuildRootCommand()
    {
        var tenantIdOption = new Option<string?>(
            aliases: ["--tenant-id", "-t"],
            description: "Entra ID tenant ID (used if login is needed).");

        var subscriptionOption = new Option<string?>(
            aliases: ["--subscription", "-s"],
            description: "Subscription name or ID (interactive if omitted).");

        var resourceGroupOption = new Option<string?>(
            aliases: ["--resource-group", "-g"],
            description: "Resource group that contains all services.");

        var cosmosAccountOption = new Option<string?>("--cosmos-account", "Cosmos DB account name.");
        var storageAccountOption = new Option<string?>("--storage-account", "Storage account name.");
        var foundryAccountOption = new Option<string?>("--foundry-account", "Microsoft Foundry account name.");
        foundryAccountOption.AddAlias("--ai-services-account");

        var foundryProjectOption = new Option<string?>(
            "--foundry-project",
            "Foundry project name used for managed identity role configuration.");

        var appConfigStoreOption = new Option<string?>("--app-config-store", "App Configuration store name.");
        var keyVaultOption = new Option<string?>("--key-vault", "Key Vault name.");
        var appInsightsOption = new Option<string?>("--app-insights", "Application Insights component name.");
        var principalIdOption = new Option<string?>("--principal-id", "Object ID of principal to assign roles to.");
        var principalNameOption = new Option<string?>("--principal-name", "Display name of principal.");

        var principalTypeOption = new Option<string>("--principal-type", () => "ServicePrincipal", "Principal type for --principal-id.");
        principalTypeOption.FromAmong("User", "ServicePrincipal", "Group");

        RootCommand root = new("Assign Azure RBAC roles for local development.");
        root.AddOption(tenantIdOption);
        root.AddOption(subscriptionOption);
        root.AddOption(resourceGroupOption);
        root.AddOption(cosmosAccountOption);
        root.AddOption(storageAccountOption);
        root.AddOption(foundryAccountOption);
        root.AddOption(foundryProjectOption);
        root.AddOption(appConfigStoreOption);
        root.AddOption(keyVaultOption);
        root.AddOption(appInsightsOption);
        root.AddOption(principalIdOption);
        root.AddOption(principalNameOption);
        root.AddOption(principalTypeOption);

        root.SetHandler((InvocationContext context) =>
        {
            RbacOptions options = new()
            {
                TenantId = context.ParseResult.GetValueForOption(tenantIdOption),
                Subscription = context.ParseResult.GetValueForOption(subscriptionOption),
                ResourceGroup = context.ParseResult.GetValueForOption(resourceGroupOption),
                CosmosAccount = context.ParseResult.GetValueForOption(cosmosAccountOption),
                StorageAccount = context.ParseResult.GetValueForOption(storageAccountOption),
                AiServicesAccount = context.ParseResult.GetValueForOption(foundryAccountOption),
                FoundryProject = context.ParseResult.GetValueForOption(foundryProjectOption),
                AppConfigStore = context.ParseResult.GetValueForOption(appConfigStoreOption),
                KeyVault = context.ParseResult.GetValueForOption(keyVaultOption),
                AppInsights = context.ParseResult.GetValueForOption(appInsightsOption),
                PrincipalId = context.ParseResult.GetValueForOption(principalIdOption),
                PrincipalName = context.ParseResult.GetValueForOption(principalNameOption),
                PrincipalType = context.ParseResult.GetValueForOption(principalTypeOption) ?? "ServicePrincipal",
            };

            context.ExitCode = RbacRunner.Run(options);
        });

        return root;
    }
}
