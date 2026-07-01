using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgenticRagScanner.DeployAgentCli.Configuration;

internal static class AgentConfigLoader
{
    public static AgentYamlConfig Load(string yamlPath)
    {
        if (!File.Exists(yamlPath))
        {
            throw new FileNotFoundException($"Agent YAML file not found: {yamlPath}");
        }

        string raw = File.ReadAllText(yamlPath);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("Agent YAML file is empty.");
        }

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var cfg = deserializer.Deserialize<AgentYamlConfig>(raw);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.Name))
        {
            throw new InvalidOperationException("Agent YAML must define a non-empty 'name'.");
        }

        return cfg;
    }
}
