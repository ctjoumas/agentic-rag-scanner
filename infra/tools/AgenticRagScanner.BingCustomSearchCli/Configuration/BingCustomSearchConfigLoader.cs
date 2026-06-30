using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgenticRagScanner.BingCustomSearchCli.Configuration;

internal static class BingCustomSearchConfigLoader
{
    public static BingCustomSearchConfig Load(string yamlPath)
    {
        if (!File.Exists(yamlPath))
        {
            throw new FileNotFoundException($"Bing Custom Search config file not found: {yamlPath}");
        }

        string raw = File.ReadAllText(yamlPath);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("Bing Custom Search config file is empty.");
        }

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var cfg = deserializer.Deserialize<BingCustomSearchConfig>(raw);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ConfigurationName))
        {
            throw new InvalidOperationException("Bing Custom Search config must define a non-empty 'configuration_name'.");
        }

        return cfg;
    }
}
