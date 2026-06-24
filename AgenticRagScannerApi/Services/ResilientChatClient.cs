using System.ClientModel;
using System.Diagnostics;
using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Core.Throttling;
using Azure;
using Microsoft.Extensions.AI;
using Polly;
using Polly.Retry;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Decorates the Foundry <see cref="IChatClient"/> with a Polly resilience pipeline (exponential
/// retry on transient failures + a per-request timeout), the shared outbound throttle, and structured
/// token/latency logging. This is the chat client every MAF agent ultimately calls. Streaming passes
/// through to the inner client; the agents in this solution use the non-streaming path.
/// </summary>
public sealed class ResilientChatClient : DelegatingChatClient
{
    private readonly ISharedThrottle _throttle;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<ResilientChatClient> _logger;

    public ResilientChatClient(
        IChatClient innerClient,
        ISharedThrottle throttle,
        FoundryOptions options,
        ILogger<ResilientChatClient> logger)
        : base(innerClient)
    {
        _throttle = throttle;
        _logger = logger;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds),
                ShouldHandle = static args => ValueTask.FromResult(IsTransient(args.Outcome.Exception)),
            })
            .AddTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds))
            .Build();
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
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
            "Foundry chat completion in {DurationMs:F0} ms (model {ModelId}); tokens in/out/total = {InputTokens}/{OutputTokens}/{TotalTokens}.",
            stopwatch.Elapsed.TotalMilliseconds,
            response.ModelId,
            usage?.InputTokenCount,
            usage?.OutputTokenCount,
            usage?.TotalTokenCount);

        return response;
    }

    private static bool IsTransient(Exception? exception) => exception switch
    {
        ClientResultException clientResult => clientResult.Status is 0 or 408 or 429 or >= 500,
        RequestFailedException requestFailed => requestFailed.Status is 0 or 408 or 429 or >= 500,
        HttpRequestException => true,
        TimeoutException => true,
        _ => false,
    };
}
