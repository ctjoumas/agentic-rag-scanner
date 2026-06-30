using YamlDotNet.Serialization;

namespace AgenticRagScanner.BingCustomSearchCli.Configuration;

internal sealed class BingCustomSearchConfig
{
    [YamlMember(Alias = "configuration_name")]
    public string ConfigurationName { get; init; } = string.Empty;

    [YamlMember(Alias = "allowed_domains")]
    public List<BingCustomSearchDomainEntry> AllowedDomains { get; init; } = [];

    [YamlMember(Alias = "blocked_domains")]
    public List<BingCustomSearchDomainEntry> BlockedDomains { get; init; } = [];
}

internal sealed class BingCustomSearchDomainEntry
{
    [YamlMember(Alias = "domain")]
    public string Domain { get; init; } = string.Empty;

    [YamlMember(Alias = "include_sub_pages")]
    public bool? IncludeSubPages { get; init; }

    [YamlMember(Alias = "boost_level")]
    public string? BoostLevel { get; init; }
}
