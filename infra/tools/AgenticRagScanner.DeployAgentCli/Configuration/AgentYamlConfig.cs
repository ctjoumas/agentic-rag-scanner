using YamlDotNet.Serialization;

namespace AgenticRagScanner.DeployAgentCli.Configuration;

internal sealed class AgentYamlConfig
{
    [YamlMember(Alias = "name")]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "version")]
    public string Version { get; init; } = "1";

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "definition")]
    public AgentDefinitionYaml Definition { get; init; } = new();

    public string ResolveModel(string? overrideModel)
    {
        if (!string.IsNullOrWhiteSpace(overrideModel))
        {
            return overrideModel;
        }

        var envModel = Environment.GetEnvironmentVariable("FOUNDRY_MODEL");
        if (!string.IsNullOrWhiteSpace(envModel))
        {
            return envModel;
        }

        if (!string.IsNullOrWhiteSpace(Definition.Model))
        {
            return Definition.Model;
        }

        throw new InvalidOperationException("Model is required. Set --model, FOUNDRY_MODEL, or definition.model in YAML.");
    }
}

internal sealed class AgentDefinitionYaml
{
    [YamlMember(Alias = "model")]
    public string Model { get; init; } = string.Empty;

    [YamlMember(Alias = "instructions")]
    public string Instructions { get; init; } = string.Empty;

    [YamlMember(Alias = "temperature")]
    public double Temperature { get; init; } = 1.0;

    [YamlMember(Alias = "top_p")]
    public double TopP { get; init; } = 1.0;

    [YamlMember(Alias = "tool_choice")]
    public string? ToolChoice { get; init; }

    [YamlMember(Alias = "reasoning")]
    public ReasoningYaml? Reasoning { get; init; }

    [YamlMember(Alias = "tools")]
    public List<AgentToolYaml> Tools { get; init; } = [];
}

internal sealed class ReasoningYaml
{
    [YamlMember(Alias = "effort")]
    public string? Effort { get; init; }
}

internal sealed class AgentToolYaml
{
    [YamlMember(Alias = "type")]
    public string Type { get; init; } = string.Empty;

    [YamlMember(Alias = "custom_search_configuration")]
    public WebSearchCustomConfigurationYaml? CustomSearchConfiguration { get; init; }
}

internal sealed class WebSearchCustomConfigurationYaml
{
    [YamlMember(Alias = "project_connection_id")]
    public string? ProjectConnectionId { get; init; }

    [YamlMember(Alias = "connection_id")]
    public string? ConnectionId { get; init; }

    [YamlMember(Alias = "connection_name")]
    public string? ConnectionName { get; init; }

    [YamlMember(Alias = "instance_name")]
    public string? InstanceName { get; init; }
}
