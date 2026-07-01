using System.Net;
using AgenticRagScannerApi.Workflows.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <inheritdoc />
public class CosmosRepository<T> : ICosmosRepository<T>
    where T : class, ICosmosEntity
{
    private readonly Container _container;
    private readonly ILogger<CosmosRepository<T>> _logger;

    public CosmosRepository(CosmosClient client, IOptions<CosmosOptions> options, ILogger<CosmosRepository<T>> logger)
    {
        var value = options.Value;

        // GetContainer returns a proxy without any network/control-plane call; the container must already exist.
        _container = client.GetContainer(value.Database, value.RegDocsContainer);
        _logger = logger;
    }

    public async Task<T> CreateAsync(T item, string partitionKey, CancellationToken cancellationToken = default)
    {
        var response = await _container
            .CreateItemAsync(item, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Cosmos document created: id '{Id}', partition '{PartitionKey}'.", item.Id, partitionKey);

        return response.Resource;
    }

    public async Task<T> UpsertAsync(T item, string partitionKey, CancellationToken cancellationToken = default)
    {
        var response = await _container
            .UpsertItemAsync(item, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Cosmos document upserted: id '{Id}', partition '{PartitionKey}'.", item.Id, partitionKey);

        return response.Resource;
    }

    public async Task<T?> ReadAsync(string id, string partitionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container
                .ReadItemAsync<T>(id, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Cosmos document not found: id '{Id}', partition '{PartitionKey}'.", id, partitionKey);

            return null;
        }
    }

    public async Task<T> ReplaceAsync(T item, string partitionKey, CancellationToken cancellationToken = default)
    {
        var response = await _container
            .ReplaceItemAsync(item, item.Id, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Cosmos document replaced: id '{Id}', partition '{PartitionKey}'.", item.Id, partitionKey);

        return response.Resource;
    }

    public async Task DeleteAsync(string id, string partitionKey, CancellationToken cancellationToken = default)
    {
        await _container
            .DeleteItemAsync<T>(id, new PartitionKey(partitionKey), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Cosmos document deleted: id '{Id}', partition '{PartitionKey}'.", id, partitionKey);
    }

    public async Task<IReadOnlyList<T>> QueryAsync(QueryDefinition query, string? partitionKey = null, CancellationToken cancellationToken = default)
    {
        var requestOptions = partitionKey is null
            ? null
            : new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) };

        var results = new List<T>();
        using var iterator = _container.GetItemQueryIterator<T>(query, requestOptions: requestOptions);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(page);
        }

        return results;
    }
}
