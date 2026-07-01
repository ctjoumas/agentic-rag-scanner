using AgenticRagScanner.DeployAgentCli.Configuration;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using OpenAI.Responses;
using System.ClientModel;

namespace AgenticRagScanner.DeployAgentCli.Services;

#pragma warning disable OPENAI001
internal sealed class AgentManager
{
    private readonly AIProjectClient _client;

    public AgentManager(string endpoint)
    {
        var credential = new ChainedTokenCredential(
            new AzureCliCredential(),
            new DefaultAzureCredential());

        _client = new AIProjectClient(new Uri(endpoint), credential);
    }

    public (ProjectsAgentVersion Version, string OperationType) EnsureAgent(
        string agentName,
        AgentYamlConfig yaml,
        string model,
        IDictionary<string, string>? metadata = null,
        string? webSearchConnectionId = null,
        string? webSearchConnectionName = null,
        string? webSearchInstanceName = null)
    {
        var definition = new DeclarativeAgentDefinition(model)
        {
            Instructions = yaml.Definition.Instructions,
        };

        foreach (var tool in yaml.Definition.Tools)
        {
            bool isBingCustomSearchTool =
                string.Equals(tool.Type, "web_search", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Type, "bing_custom_search", StringComparison.OrdinalIgnoreCase);

            if (!isBingCustomSearchTool)
            {
                throw new NotSupportedException(
                    $"Unsupported tool type '{tool.Type}'. This CLI currently supports only the Bing Custom Search tool.");
            }

            var custom = tool.CustomSearchConfiguration;
            string? resolvedConnectionId =
                custom?.ProjectConnectionId ??
                custom?.ConnectionId ??
                webSearchConnectionId;

            if (string.IsNullOrWhiteSpace(resolvedConnectionId))
            {
                string? connectionName = custom?.ConnectionName ?? webSearchConnectionName;
                if (string.IsNullOrWhiteSpace(connectionName))
                {
                    throw new InvalidOperationException(
                        "Bing Custom Search tool requires a connection. Provide custom_search_configuration.project_connection_id, connection_id, connection_name, or pass --bing-custom-search-connection-id/--bing-custom-search-connection-name.");
                }

                resolvedConnectionId = ResolveConnectionId(connectionName);
            }

            string? resolvedInstanceName = custom?.InstanceName ?? webSearchInstanceName;
            if (string.IsNullOrWhiteSpace(resolvedInstanceName))
            {
                throw new InvalidOperationException(
                    "Bing Custom Search tool requires instance_name. Provide custom_search_configuration.instance_name or pass --bing-custom-search-instance-name.");
            }

            WebSearchTool webSearchTool = ResponseTool.CreateWebSearchTool();
            webSearchTool.CustomSearchConfiguration = new ProjectWebSearchConfiguration(resolvedConnectionId, resolvedInstanceName);
            definition.Tools.Add(webSearchTool);
        }

        var createOptions = new ProjectsAgentVersionCreationOptions(definition)
        {
            Description = yaml.Description,
        };

        if (metadata is not null)
        {
            foreach (var kvp in metadata)
            {
                createOptions.Metadata[kvp.Key] = kvp.Value;
            }
        }

        string operationType = Exists(agentName) ? "updated" : "created";
        ProjectsAgentVersion version = _client.AgentAdministrationClient.CreateAgentVersion(agentName, createOptions);
        return (version, operationType);
    }

    public IReadOnlyList<AgentListItem> ListAgents()
    {
        var items = new List<AgentListItem>();
        foreach (var agent in _client.AgentAdministrationClient.GetAgents())
        {
            items.Add(new AgentListItem(agent.Id, agent.Name));
        }

        return items;
    }

    public void DeleteAgent(string agentName)
    {
        try
        {
            _client.AgentAdministrationClient.DeleteAgent(agentName);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            // Keep parity with Python behavior: deleting a missing agent is a no-op.
        }
    }

    private bool Exists(string agentName)
    {
        try
        {
            _client.AgentAdministrationClient.GetAgent(agentName);
            return true;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    private string ResolveConnectionId(string connectionName)
    {
        AIProjectConnection connection = _client.Connections.GetConnection(connectionName);
        if (string.IsNullOrWhiteSpace(connection.Id))
        {
            throw new InvalidOperationException($"Foundry connection '{connectionName}' has no id.");
        }

        return connection.Id;
    }
}

internal sealed record AgentListItem(string Id, string Name);
#pragma warning restore OPENAI001
