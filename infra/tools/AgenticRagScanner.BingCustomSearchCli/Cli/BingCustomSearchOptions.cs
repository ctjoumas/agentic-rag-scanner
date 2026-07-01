namespace AgenticRagScanner.BingCustomSearchCli.Cli;

internal sealed class BingCustomSearchOptions
{
    public required string Command { get; init; }

    public string OutputFormat { get; init; } = "human";

    public string LogLevel { get; init; } = "information";

    public string? SubscriptionId { get; init; }

    public string? ResourceGroup { get; init; }

    public string? BingAccountName { get; init; }

    public string? BingConfigurationPath { get; init; }

    public string? FoundryAccountName { get; init; }

    public string? FoundryProjectName { get; init; }

    public string? ConnectionName { get; init; }

    public string? ConnectionDisplayName { get; init; }
}
