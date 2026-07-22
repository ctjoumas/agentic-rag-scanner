using System.Diagnostics;
using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Core.Throttling;
using Microsoft.Extensions.AI;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Decorates the Foundry <see cref="IChatClient"/> with a Polly resilience pipeline (exponential
/// retry on transient failures - honoring a server <c>Retry-After</c> hint - a circuit breaker that fails
/// fast when the endpoint is repeatedly overloaded, and a per-request timeout), the shared outbound
/// throttle, and structured token/latency logging. This is the chat client every MAF agent ultimately
/// calls. Streaming passes through to the inner client; the agents in this solution use the non-streaming path.
/// </summary>
public sealed class ResilientChatClient : DelegatingChatClient
{
    private readonly ISharedThrottle _throttle;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<ResilientChatClient> _logger;
    private readonly string _modelDeploymentName;

    public ResilientChatClient(
        IChatClient innerClient,
        ISharedThrottle throttle,
        FoundryOptions options,
        ILogger<ResilientChatClient> logger,
        string modelDeploymentName)
        : base(innerClient)
    {
        _throttle = throttle;
        _logger = logger;
        _modelDeploymentName = modelDeploymentName;

        var respectRetryAfter = options.RespectRetryAfter;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds),
                ShouldHandle = static args => ValueTask.FromResult(ResilienceHelpers.IsTransient(args.Outcome.Exception)),
                // Honor a server Retry-After (e.g. on 429/529) over the computed backoff; returning null
                // falls back to the exponential-with-jitter delay for this attempt.
                DelayGenerator = respectRetryAfter
                    ? args => ValueTask.FromResult(ResilienceHelpers.TryGetRetryAfter(args.Outcome.Exception))
                    : null,
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.CircuitBreakerFailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingSeconds),
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakSeconds),
                ShouldHandle = static args => ValueTask.FromResult(ResilienceHelpers.IsTransient(args.Outcome.Exception)),
            })
            .AddTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds))
            .Build();
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "MAF chat completion request to Foundry model deployment '{ModelDeploymentName}'.",
            _modelDeploymentName);

        var stopwatch = Stopwatch.StartNew();

        var response = await _pipeline.ExecuteAsync(
            async token => await _throttle.ExecuteAsync(
                t => base.GetResponseAsync(messages, options, t),
                permits: 1,
                cancellationToken: token),
            cancellationToken);

        stopwatch.Stop();

        var usage = response.Usage;
        _logger.LogInformation(
            "MAF chat completion for Foundry model deployment '{ModelDeploymentName}' completed in {DurationMs:F0} ms (service-reported model {ModelId}); tokens in/out/total = {InputTokens}/{OutputTokens}/{TotalTokens}.",
            _modelDeploymentName,
            stopwatch.Elapsed.TotalMilliseconds,
            response.ModelId,
            usage?.InputTokenCount,
            usage?.OutputTokenCount,
            usage?.TotalTokenCount);

        return response;
    }
}
