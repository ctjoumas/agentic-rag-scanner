using System.Net;
using System.Text.Json;
using AgenticRagScannerApi.Workflows.Configuration;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Workflows.Checkpointing;

/// <summary>
/// A MAF <see cref="JsonCheckpointStore"/> backed by Azure Cosmos DB, connected keyless with
/// <c>DefaultAzureCredential</c>. Each checkpoint is one document in the configured <c>checkpoints</c>
/// container, partitioned by checkpoint session id, so a run is resumable from any persisted
/// super-step. Wire it into MAF via <c>CheckpointManager.CreateJson(store, options)</c>.
/// </summary>
/// <remarks>
/// The database and container are expected to already exist (provisioned via IaC/CLI/portal).
/// Keyless data-plane access intentionally does not create them - container creation is a
/// control-plane operation that data-plane RBAC does not grant.
/// </remarks>
public sealed class CosmosCheckpointStore : JsonCheckpointStore
{
    private readonly Container _container;
    private readonly ILogger<CosmosCheckpointStore> _logger;

    public CosmosCheckpointStore(CosmosClient client, IOptions<CosmosOptions> options, ILogger<CosmosCheckpointStore> logger)
    {
        var value = options.Value;

        // GetContainer returns a proxy without any network/control-plane call; the container must exist.
        _container = client.GetContainer(value.Database, value.CheckpointsContainer);
        _logger = logger;
    }

    /// <inheritdoc />
    public override async ValueTask<CheckpointInfo> CreateCheckpointAsync(string sessionId, JsonElement value, CheckpointInfo? parent = null)
    {
        var checkpointId = Guid.NewGuid().ToString("N");

        var document = new CheckpointDocument
        {
            Id = DocumentId(sessionId, checkpointId),
            SessionId = sessionId,
            CheckpointId = checkpointId,
            ParentCheckpointId = parent?.CheckpointId,
            ValueJson = value.GetRawText(),
        };

        await _container.UpsertItemAsync(document, new PartitionKey(sessionId)).ConfigureAwait(false);

        _logger.LogDebug("Cosmos checkpoint created: session '{SessionId}', checkpoint '{CheckpointId}'.", sessionId, checkpointId);

        return new CheckpointInfo(sessionId, checkpointId);
    }

    /// <inheritdoc />
    public override async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        try
        {
            var response = await _container
                .ReadItemAsync<CheckpointDocument>(DocumentId(sessionId, key.CheckpointId), new PartitionKey(sessionId))
                .ConfigureAwait(false);

            using var document = JsonDocument.Parse(response.Resource.ValueJson);
            return document.RootElement.Clone();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException($"Checkpoint '{key.CheckpointId}' not found for session '{sessionId}'.", ex);
        }
    }

    /// <inheritdoc />
    public override async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent = null)
    {
        var sql = "SELECT c.sessionId, c.checkpointId, c.parentCheckpointId FROM c WHERE c.sessionId = @sessionId";
        if (withParent is not null)
        {
            sql += " AND c.parentCheckpointId = @parentId";
        }

        var query = new QueryDefinition(sql).WithParameter("@sessionId", sessionId);
        if (withParent is not null)
        {
            query = query.WithParameter("@parentId", withParent.CheckpointId);
        }

        var results = new List<CheckpointInfo>();
        using var iterator = _container.GetItemQueryIterator<CheckpointDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(sessionId) });

        while (iterator.HasMoreResults)
        {
            foreach (var document in await iterator.ReadNextAsync().ConfigureAwait(false))
            {
                results.Add(new CheckpointInfo(document.SessionId, document.CheckpointId));
            }
        }

        return results;
    }

    private static string DocumentId(string sessionId, string checkpointId) => $"{sessionId}:{checkpointId}";
}
