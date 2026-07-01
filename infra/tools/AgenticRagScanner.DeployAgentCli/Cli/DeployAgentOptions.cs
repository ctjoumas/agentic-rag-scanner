namespace AgenticRagScanner.DeployAgentCli.Cli;

internal sealed class DeployAgentOptions
{
    public required string Command { get; init; }

    public required string Endpoint { get; init; }

    public string OutputFormat { get; init; } = "human";

    public string LogLevel { get; init; } = "information";

    public string? YamlPath { get; init; }

    public string? AgentName { get; init; }

    public string? Model { get; init; }

    public string? WebSearchConnectionId { get; init; }

    public string? WebSearchConnectionName { get; init; }

    public string? WebSearchInstanceName { get; init; }

    public string? DeleteAgentName { get; init; }
}
