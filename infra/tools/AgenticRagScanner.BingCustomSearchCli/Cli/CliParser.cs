using System.CommandLine;
using System.CommandLine.Invocation;
using AgenticRagScanner.BingCustomSearchCli.Runner;

namespace AgenticRagScanner.BingCustomSearchCli.Cli;

internal static class CliParser
{
    public static int Invoke(string[] args)
    {
        return BuildRootCommand().Invoke(args);
    }

    private static RootCommand BuildRootCommand()
    {
        var outputFormatOption = new Option<string>("--output-format", () => "human");
        outputFormatOption.FromAmong("human", "json");

        var logLevelOption = new Option<string>("--log-level", () => "information");
        logLevelOption.FromAmong("error", "warning", "information", "verbose");

        var subscriptionOption = new Option<string?>("--subscription", () => Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID"));
        var resourceGroupOption = new Option<string?>("--resource-group", () => Environment.GetEnvironmentVariable("AZURE_RESOURCE_GROUP"));
        var bingAccountNameOption = new Option<string?>(
            aliases: ["--bing-account-name", "--account-name"],
            getDefaultValue: () => Environment.GetEnvironmentVariable("BINGCUSTOMSEARCHACCOUNTNAME"));
        var bingConfigurationPathOption = new Option<string?>("--bing-configuration-path", () => null);
        var foundryAccountNameOption = new Option<string?>("--foundry-account-name", () => Environment.GetEnvironmentVariable("FOUNDRYNAME"));
        var foundryProjectNameOption = new Option<string?>("--foundry-project-name", () => Environment.GetEnvironmentVariable("FOUNDRYPROJECTNAME"));
        var connectionNameOption = new Option<string?>("--connection-name", () => null);
        var connectionDisplayNameOption = new Option<string?>("--connection-display-name", () => "Bing Custom Search");

        var root = new RootCommand("Manage Bing Custom Search configurations and Foundry project connections.");
        var upsert = new Command("upsert", "Create or update a Bing Custom Search configuration.");

        upsert.AddOption(subscriptionOption);
        upsert.AddOption(resourceGroupOption);
        upsert.AddOption(bingAccountNameOption);
        upsert.AddOption(bingConfigurationPathOption);
        upsert.AddOption(outputFormatOption);
        upsert.AddOption(logLevelOption);

        upsert.SetHandler((InvocationContext context) =>
        {
            var subscriptionId = context.ParseResult.GetValueForOption(subscriptionOption);
            var resourceGroup = context.ParseResult.GetValueForOption(resourceGroupOption);
            var accountName = context.ParseResult.GetValueForOption(bingAccountNameOption);

            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                context.Console.Error.Write("Azure subscription is required. Use --subscription or set AZURE_SUBSCRIPTION_ID.\n");
                context.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(resourceGroup))
            {
                context.Console.Error.Write("Azure resource group is required. Use --resource-group or set AZURE_RESOURCE_GROUP.\n");
                context.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(accountName))
            {
                context.Console.Error.Write("Bing account name is required. Use --bing-account-name or set BINGCUSTOMSEARCHACCOUNTNAME.\n");
                context.ExitCode = 1;
                return;
            }

            var options = new BingCustomSearchOptions
            {
                Command = "upsert",
                OutputFormat = context.ParseResult.GetValueForOption(outputFormatOption) ?? "human",
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "information",
                SubscriptionId = subscriptionId,
                ResourceGroup = resourceGroup,
                BingAccountName = accountName,
                BingConfigurationPath = context.ParseResult.GetValueForOption(bingConfigurationPathOption),
            };

            context.ExitCode = BingCustomSearchRunner.Run(options);
        });

        root.AddCommand(upsert);

        var createConnection = new Command("create-connection", "Create a Foundry project connection for Bing Custom Search.");
        createConnection.AddOption(subscriptionOption);
        createConnection.AddOption(resourceGroupOption);
        createConnection.AddOption(bingAccountNameOption);
        createConnection.AddOption(foundryAccountNameOption);
        createConnection.AddOption(foundryProjectNameOption);
        createConnection.AddOption(connectionNameOption);
        createConnection.AddOption(connectionDisplayNameOption);
        createConnection.AddOption(outputFormatOption);
        createConnection.AddOption(logLevelOption);

        createConnection.SetHandler((InvocationContext context) =>
        {
            var subscriptionId = context.ParseResult.GetValueForOption(subscriptionOption);
            var resourceGroup = context.ParseResult.GetValueForOption(resourceGroupOption);
            var bingAccountName = context.ParseResult.GetValueForOption(bingAccountNameOption);
            var foundryAccountName = context.ParseResult.GetValueForOption(foundryAccountNameOption);
            var foundryProjectName = context.ParseResult.GetValueForOption(foundryProjectNameOption);
            var connectionName = context.ParseResult.GetValueForOption(connectionNameOption);
            var connectionDisplayName = context.ParseResult.GetValueForOption(connectionDisplayNameOption);

            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                context.Console.Error.Write("Azure subscription is required. Use --subscription or set AZURE_SUBSCRIPTION_ID.\n");
                context.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(resourceGroup))
            {
                context.Console.Error.Write("Azure resource group is required. Use --resource-group or set AZURE_RESOURCE_GROUP.\n");
                context.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(bingAccountName))
            {
                context.Console.Error.Write("Bing account name is required. Use --bing-account-name or set BINGCUSTOMSEARCHACCOUNTNAME.\n");
                context.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(foundryAccountName))
            {
                context.Console.Error.Write("Foundry account name is required. Use --foundry-account-name or set FOUNDRYNAME.\n");
                context.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(foundryProjectName))
            {
                context.Console.Error.Write("Foundry project name is required. Use --foundry-project-name or set FOUNDRYPROJECTNAME.\n");
                context.ExitCode = 1;
                return;
            }

            var options = new BingCustomSearchOptions
            {
                Command = "create-connection",
                OutputFormat = context.ParseResult.GetValueForOption(outputFormatOption) ?? "human",
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "information",
                SubscriptionId = subscriptionId,
                ResourceGroup = resourceGroup,
                BingAccountName = bingAccountName,
                FoundryAccountName = foundryAccountName,
                FoundryProjectName = foundryProjectName,
                ConnectionName = connectionName ?? bingAccountName,
                ConnectionDisplayName = connectionDisplayName ?? "Bing Custom Search",
            };

            context.ExitCode = BingCustomSearchRunner.Run(options);
        });

        root.AddCommand(createConnection);
        return root;
    }
}
