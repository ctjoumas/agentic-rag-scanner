using System.Text.Json;
using AgenticRagScanner.BingCustomSearchCli.Cli;
using AgenticRagScanner.BingCustomSearchCli.Configuration;
using AgenticRagScanner.BingCustomSearchCli.Services;

namespace AgenticRagScanner.BingCustomSearchCli.Runner;

internal static class BingCustomSearchRunner
{
    public static int Run(BingCustomSearchOptions options)
    {
        try
        {
            return options.Command switch
            {
                "upsert" => Upsert(options),
                "create-connection" => CreateConnection(options),
                _ => 1,
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Upsert(BingCustomSearchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SubscriptionId) ||
            string.IsNullOrWhiteSpace(options.ResourceGroup) ||
            string.IsNullOrWhiteSpace(options.BingAccountName))
        {
            Console.Error.WriteLine("Bing config upsert requires subscription, resource group, and Bing account name.");
            return 1;
        }

        string configPath = options.BingConfigurationPath ?? GetDefaultBingConfigurationPath();
        BingCustomSearchConfig config = BingCustomSearchConfigLoader.Load(configPath);
        var manager = new BingCustomSearchManager();
        BingCustomSearchUpsertResult result = manager.UpsertConfigurationAsync(
            options.SubscriptionId,
            options.ResourceGroup,
            options.BingAccountName,
            config).GetAwaiter().GetResult();

        WriteOutput(options, new
        {
            success = true,
            operationType = result.OperationType,
            accountName = options.BingAccountName,
            configurationName = result.ConfigurationName,
        });
        return 0;
    }

    private static int CreateConnection(BingCustomSearchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SubscriptionId) ||
            string.IsNullOrWhiteSpace(options.ResourceGroup) ||
            string.IsNullOrWhiteSpace(options.BingAccountName) ||
            string.IsNullOrWhiteSpace(options.FoundryAccountName) ||
            string.IsNullOrWhiteSpace(options.FoundryProjectName) ||
            string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            Console.Error.WriteLine("Create connection requires subscription, resource group, Bing account name, Foundry account name, Foundry project name, and connection name.");
            return 1;
        }

        var manager = new BingCustomSearchManager();
        BingCustomSearchConnectionResult result = manager.CreateFoundryConnectionAsync(
            options.SubscriptionId,
            options.ResourceGroup,
            options.BingAccountName,
            options.FoundryAccountName,
            options.FoundryProjectName,
            options.ConnectionName,
            options.ConnectionDisplayName ?? "Bing Custom Search").GetAwaiter().GetResult();

        WriteOutput(options, new
        {
            success = true,
            operationType = result.OperationType,
            connectionId = result.ConnectionId,
            connectionName = result.ConnectionName,
            projectName = options.FoundryProjectName,
        });
        return 0;
    }

    private static string GetDefaultBingConfigurationPath()
    {
        string repoRoot = ResolveRepoRoot();
        return Path.Combine(repoRoot, "infra", "tools", "AgenticRagScanner.BingCustomSearchCli", "Configuration", "bing-custom-search.yaml");
    }

    private static string ResolveRepoRoot()
    {
        string? current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "AgenticRagScannerApi.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root (AgenticRagScannerApi.sln).");
    }

    private static void WriteOutput(BingCustomSearchOptions options, object payload)
    {
        if (options.OutputFormat == "json")
        {
            Console.WriteLine($"BING_CUSTOM_SEARCH_RESULT: {JsonSerializer.Serialize(payload)}");
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
