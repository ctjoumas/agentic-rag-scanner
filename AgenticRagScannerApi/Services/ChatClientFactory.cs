using System.Collections.Concurrent;
using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Core.Throttling;
using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Default <see cref="IChatClientFactory"/>. Constructs one shared <see cref="AzureOpenAIClient"/> against
/// the Foundry endpoint (keyless via <see cref="TokenCredential"/>, or an API key for local dev), then
/// builds one resilient <see cref="IChatClient"/> per model deployment - each wrapped with the shared
/// throttle + Polly resilience pipeline (<see cref="ResilientChatClient"/>) and OpenTelemetry GenAI
/// instrumentation. Built clients are cached so repeated resolutions for the same deployment are cheap.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    private readonly AzureOpenAIClient _azureClient;
    private readonly FoundryOptions _options;
    private readonly ISharedThrottle _throttle;
    private readonly ILogger<ResilientChatClient> _resilientLogger;
    private readonly ILogger<ChatClientFactory> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IChatClient> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ChatClientFactory(
        IOptions<FoundryOptions> options,
        TokenCredential credential,
        ISharedThrottle throttle,
        ILogger<ResilientChatClient> resilientLogger,
        ILogger<ChatClientFactory> logger,
        IServiceProvider serviceProvider)
    {
        _options = options.Value;
        _throttle = throttle;
        _resilientLogger = resilientLogger;
        _logger = logger;
        _serviceProvider = serviceProvider;

        _azureClient = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? new AzureOpenAIClient(new Uri(_options.Endpoint), credential)
            : new AzureOpenAIClient(new Uri(_options.Endpoint), new AzureKeyCredential(_options.ApiKey));
    }

    public IChatClient Create(string modelDeploymentName)
    {
        if (string.IsNullOrWhiteSpace(modelDeploymentName))
        {
            throw new ArgumentException("Model deployment name must be provided.", nameof(modelDeploymentName));
        }

        return _cache.GetOrAdd(modelDeploymentName, BuildClient);
    }

    private IChatClient BuildClient(string modelDeploymentName)
    {
        _logger.LogInformation(
            "Building Foundry chat client for model deployment '{ModelDeploymentName}' (endpoint '{Endpoint}').",
            modelDeploymentName,
            _options.Endpoint);

        IChatClient inner = _azureClient
            .GetChatClient(modelDeploymentName)
            .AsIChatClient();

        IChatClient resilient = new ResilientChatClient(inner, _throttle, _options, _resilientLogger, modelDeploymentName);

        return new ChatClientBuilder(resilient)
            .UseOpenTelemetry()
            .Build(_serviceProvider);
    }
}
