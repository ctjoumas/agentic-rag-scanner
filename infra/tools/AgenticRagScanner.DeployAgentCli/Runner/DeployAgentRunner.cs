using System.Text.Json;
using AgenticRagScanner.DeployAgentCli.Configuration;
using AgenticRagScanner.DeployAgentCli.Cli;
using AgenticRagScanner.DeployAgentCli.Services;

namespace AgenticRagScanner.DeployAgentCli.Runner;

internal static class DeployAgentRunner
{
    public static int Run(DeployAgentOptions options)
    {
        try
        {
            ValidateEndpoint(options.Endpoint);
            var manager = new AgentManager(options.Endpoint);

            return options.Command switch
            {
                "deploy" => Deploy(manager, options),
                "list" => List(manager, options),
                "delete" => Delete(manager, options),
                _ => 1,
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Deploy(AgentManager manager, DeployAgentOptions options)
    {
        string yamlPath = options.YamlPath ?? PromptForYamlPath(GetDefaultYamlPath());
        AgentYamlConfig yaml = AgentConfigLoader.Load(yamlPath);

        string agentName = string.IsNullOrWhiteSpace(options.AgentName) ? yaml.Name : options.AgentName;
        string model = yaml.ResolveModel(options.Model);

        var metadata = new Dictionary<string, string>
        {
            ["created_by"] = "DeployAgentCli",
            ["yaml_version"] = yaml.Version,
            ["deployment_method"] = "csharp-cli",
            ["created_date"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        var (version, operationType) = manager.EnsureAgent(
            agentName,
            yaml,
            model,
            metadata,
            options.WebSearchConnectionId,
            options.WebSearchConnectionName,
            options.WebSearchInstanceName);
        WriteOutput(
            options,
            new
            {
                success = true,
                operationType,
                agentName = version.Name,
                agentVersion = version.Version,
                agentId = version.Id,
                model,
            });
        return 0;
    }

    private static int List(AgentManager manager, DeployAgentOptions options)
    {
        var items = manager.ListAgents();
        if (options.OutputFormat == "json")
        {
            WriteOutput(options, new { success = true, count = items.Count, agents = items.Select(a => new { id = a.Id, name = a.Name }) });
            return 0;
        }

        if (items.Count == 0)
        {
            Console.WriteLine("No agents found.");
            return 0;
        }

        foreach (var item in items)
        {
            Console.WriteLine($"{item.Id}  name={item.Name}");
        }
        return 0;
    }

    private static int Delete(AgentManager manager, DeployAgentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DeleteAgentName))
        {
            Console.Error.WriteLine("Delete command requires an agent name.");
            return 1;
        }

        manager.DeleteAgent(options.DeleteAgentName);
        WriteOutput(options, new { success = true, operationType = "deleted", agentName = options.DeleteAgentName });
        return 0;
    }

    private static void ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("Invalid Foundry project endpoint URL.");
        }
    }

    private static string PromptForYamlPath(string defaultPath)
    {
        if (Console.IsInputRedirected)
        {
            return defaultPath;
        }

        Console.Write($"Path to agent YAML file [{defaultPath}]: ");
        string? entered = Console.ReadLine();
        return string.IsNullOrWhiteSpace(entered) ? defaultPath : entered.Trim();
    }

    private static string GetDefaultYamlPath()
    {
        string repoRoot = ResolveRepoRoot();
        return Path.Combine(repoRoot, "infra", "tools", "AgenticRagScanner.DeployAgentCli", "Configuration", "bing-grounding-agent.yaml");
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

    private static void WriteOutput(DeployAgentOptions options, object payload)
    {
        if (options.OutputFormat == "json")
        {
            Console.WriteLine($"AGENT_DEPLOYMENT_RESULT: {JsonSerializer.Serialize(payload)}");
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
