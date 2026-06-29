namespace AgenticRagScanner.RbacCli.Cli;

internal sealed class RbacOptions
{
    public string? TenantId { get; init; }

    public string? Subscription { get; init; }

    public string? ResourceGroup { get; init; }

    public string? CosmosAccount { get; init; }

    public string? StorageAccount { get; init; }

    public string? AiServicesAccount { get; init; }

    public string? FoundryProject { get; init; }

    public string? AppConfigStore { get; init; }

    public string? KeyVault { get; init; }

    public string? AppInsights { get; init; }

    public string? PrincipalId { get; init; }

    public string? PrincipalName { get; init; }

    public string PrincipalType { get; init; } = "ServicePrincipal";
}