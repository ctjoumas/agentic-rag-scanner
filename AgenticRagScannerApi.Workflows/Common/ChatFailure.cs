using System.ClientModel;
using System.Text.Json;
using Azure;

namespace AgenticRagScannerApi.Workflows.Common;

/// <summary>
/// Classifies an exception thrown by a finalize-stage enrichment model call as "degradable" — safe to
/// swallow so the agent can return a graceful fallback (null impact area, empty tags, or an aggregate-only
/// Company View record) instead of faulting the whole scan.
/// <para>
/// Only two families degrade: (1) malformed model output (<see cref="JsonException"/>,
/// <see cref="InvalidOperationException"/>), which a fallback handles cleanly, and (2) the
/// <em>transient</em> infrastructure surface that <c>ResilientChatClient</c> may re-throw once its retries
/// are exhausted — HTTP status 0/408/429/>=500 plus <see cref="HttpRequestException"/>,
/// <see cref="TimeoutException"/>, and HTTP-timeout style <see cref="TaskCanceledException"/>.
/// </para>
/// <para>
/// Deterministic failures are deliberately NOT degradable and are left to propagate so they surface
/// loudly instead of quietly under-enriching every group: hard HTTP responses such as 400/401/403/404
/// (misconfiguration, auth, bad request) and genuine caller cancellation. Such failures would repeat on
/// every group and every retry, so failing the run is the correct signal.
/// </para>
/// </summary>
public static class ChatFailure
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> should be swallowed in favor of a
    /// graceful fallback; <see langword="false"/> when it should propagate (deterministic failure or
    /// genuine caller cancellation).
    /// </summary>
    public static bool IsDegradable(Exception exception, CancellationToken cancellationToken)
    {
        // Genuine caller cancellation must abort the run, not degrade to an empty result.
        if (cancellationToken.IsCancellationRequested && exception is OperationCanceledException)
        {
            return false;
        }

        return exception switch
        {
            // Malformed model output — a fallback handles this cleanly.
            JsonException or InvalidOperationException => true,

            // Transient service/infrastructure surface (mirrors ResilientChatClient.IsTransient). Hard
            // responses (400/401/403/404) fall through to false so they propagate and surface loudly.
            ClientResultException clientResult => IsTransientStatus(clientResult.Status),
            RequestFailedException requestFailed => IsTransientStatus(requestFailed.Status),
            HttpRequestException => true,
            TimeoutException => true,
            TaskCanceledException => true,

            _ => false,
        };
    }

    private static bool IsTransientStatus(int status) => status is 0 or 408 or 429 or >= 500;
}

