using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRagScanner.BingCustomSearchCli.Configuration;
using Azure.Core;
using Azure.Identity;

namespace AgenticRagScanner.BingCustomSearchCli.Services;

internal sealed class BingCustomSearchManager
{
    private const string ArmScope = "https://management.azure.com/.default";
    private const string ArmBase = "https://management.azure.com";
    private const string ProviderApiVersion = "2021-04-01";
    private const string ConfigApiVersion = "2025-05-01-preview";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HashSet<string> AllowedBoostLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default",
        "Elevated",
        "Demoted",
    };

    private readonly TokenCredential _credential = new DefaultAzureCredential();

    public async Task<BingCustomSearchUpsertResult> UpsertConfigurationAsync(
        string subscriptionId,
        string resourceGroup,
        string accountName,
        BingCustomSearchConfig config,
        CancellationToken cancellationToken = default)
    {
        ValidateConfig(config);

        string token = await GetArmTokenAsync(cancellationToken);
        await EnsureProviderRegisteredAsync(token, subscriptionId, cancellationToken);

        bool existed = await ConfigExistsAsync(token, subscriptionId, resourceGroup, accountName, config.ConfigurationName, cancellationToken);

        object payload = new
        {
            properties = new
            {
                blockedDomains = config.BlockedDomains,
                allowedDomains = config.AllowedDomains,
            },
        };

        JsonDocument? response = await PutAsync(
            $"{ArmBase}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Bing/accounts/{accountName}/customSearchConfigurations/{config.ConfigurationName}?api-version={ConfigApiVersion}",
            token,
            payload,
            cancellationToken);

        return new BingCustomSearchUpsertResult(config.ConfigurationName, existed ? "updated" : "created", response?.RootElement.Clone());
    }

    private async Task<string> GetArmTokenAsync(CancellationToken cancellationToken)
    {
        AccessToken token = await _credential.GetTokenAsync(new TokenRequestContext([ArmScope]), cancellationToken);
        return token.Token;
    }

    private static void ValidateConfig(BingCustomSearchConfig config)
    {
        ValidateDomains(config.AllowedDomains, "allowed_domains");
        ValidateDomains(config.BlockedDomains, "blocked_domains");
    }

    private static void ValidateDomains(IReadOnlyList<BingCustomSearchDomainEntry> domains, string label)
    {
        foreach (var entry in domains)
        {
            if (string.IsNullOrWhiteSpace(entry.Domain))
            {
                throw new InvalidOperationException($"{label}: each entry requires a non-empty 'domain'.");
            }

            if (entry.IncludeSubPages is null)
            {
                throw new InvalidOperationException($"{label}: domain '{entry.Domain}' must set include_sub_pages to true or false.");
            }

            if (!string.IsNullOrWhiteSpace(entry.BoostLevel))
            {
                if (!AllowedBoostLevels.Contains(entry.BoostLevel))
                {
                    throw new InvalidOperationException($"{label}: domain '{entry.Domain}' has invalid boost_level '{entry.BoostLevel}'. Allowed values: Default, Elevated, Demoted.");
                }

                if (entry.IncludeSubPages == false)
                {
                    throw new InvalidOperationException($"{label}: domain '{entry.Domain}' sets boost_level but include_sub_pages is false.");
                }
            }
        }
    }

    private async Task EnsureProviderRegisteredAsync(string token, string subscriptionId, CancellationToken cancellationToken)
    {
        JsonDocument? provider = await GetAsync(
            $"{ArmBase}/subscriptions/{subscriptionId}/providers/Microsoft.Bing?api-version={ProviderApiVersion}",
            token,
            cancellationToken,
            throwOnNotFound: false);

        string? state = provider?.RootElement.TryGetProperty("registrationState", out JsonElement registrationState) == true
            ? registrationState.GetString()
            : null;

        if (!string.Equals(state, "Registered", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Provider 'Microsoft.Bing' is not registered (state={state ?? "unknown"}). Run: az provider register --namespace Microsoft.Bing --subscription {subscriptionId}");
        }
    }

    private async Task<bool> ConfigExistsAsync(string token, string subscriptionId, string resourceGroup, string accountName, string configurationName, CancellationToken cancellationToken)
    {
        JsonDocument? response = await GetAsync(
            $"{ArmBase}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Bing/accounts/{accountName}/customSearchConfigurations/{configurationName}?api-version={ConfigApiVersion}",
            token,
            cancellationToken,
            throwOnNotFound: false);

        return response is not null;
    }

    private static async Task<JsonDocument?> GetAsync(string url, string token, CancellationToken cancellationToken, bool throwOnNotFound)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && !throwOnNotFound)
        {
            return null;
        }

        if ((int)response.StatusCode >= 400)
        {
            throw new HttpRequestException($"GET {url} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(payload) ? null : JsonDocument.Parse(payload);
    }

    private static async Task<JsonDocument?> PutAsync(string url, string token, object payload, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PutAsync(url, content, cancellationToken);

        if ((int)response.StatusCode >= 400)
        {
            throw new HttpRequestException($"PUT {url} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(responseBody) ? null : JsonDocument.Parse(responseBody);
    }

    public async Task<BingCustomSearchConnectionResult> CreateFoundryConnectionAsync(
        string subscriptionId,
        string resourceGroup,
        string bingAccountName,
        string foundryAccountName,
        string foundryProjectName,
        string connectionName,
        string connectionDisplayName,
        CancellationToken cancellationToken = default)
    {
        string token = await GetArmTokenAsync(cancellationToken);

        // Resolve API keys from the provisioned Bing Grounding account.
        string bingKey = await GetBingApiKeyAsync(token, subscriptionId, resourceGroup, bingAccountName, cancellationToken);

        // Grounding with custom search connection for Foundry project.
        object payload = new
        {
            properties = new
            {
                authType = "ApiKey",
                category = "GroundingWithCustomSearch",
                target = "https://api.bing.microsoft.com",
                credentials = new
                {
                    key = bingKey,
                },
                metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Location"] = "global",
                    ["DisplayName"] = connectionDisplayName,
                },
            },
        };

        JsonDocument? response = await PutAsync(
            $"{ArmBase}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.CognitiveServices/accounts/{foundryAccountName}/projects/{foundryProjectName}/connections/{connectionName}?api-version=2025-09-01",
            token,
            payload,
            cancellationToken);

        string connectionId = connectionName;
        if (response?.RootElement.TryGetProperty("id", out JsonElement idElement) == true)
        {
            connectionId = idElement.GetString() ?? connectionName;
        }

        return new BingCustomSearchConnectionResult(connectionName, connectionId, "created");
    }

    private async Task<string> GetBingApiKeyAsync(
        string token,
        string subscriptionId,
        string resourceGroup,
        string bingAccountName,
        CancellationToken cancellationToken)
    {
        JsonDocument? keys = await PostAsync(
            $"{ArmBase}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Bing/accounts/{bingAccountName}/listKeys?api-version=2020-06-10",
            token,
            cancellationToken);

        if (keys is null)
        {
            throw new InvalidOperationException($"Bing listKeys returned no payload for account '{bingAccountName}'.");
        }

        if (keys.RootElement.TryGetProperty("key1", out JsonElement key1Element))
        {
            string? key1 = key1Element.GetString();
            if (!string.IsNullOrWhiteSpace(key1))
            {
                return key1;
            }
        }

        throw new InvalidOperationException($"Bing listKeys payload did not contain 'key1' for account '{bingAccountName}'.");
    }

    private static async Task<JsonDocument?> PostAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(url, content, cancellationToken);

        if ((int)response.StatusCode >= 400)
        {
            throw new HttpRequestException($"POST {url} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(responseBody) ? null : JsonDocument.Parse(responseBody);
    }
}

internal sealed record BingCustomSearchUpsertResult(string ConfigurationName, string OperationType, JsonElement? Response);
internal sealed record BingCustomSearchConnectionResult(string ConnectionName, string ConnectionId, string OperationType);
