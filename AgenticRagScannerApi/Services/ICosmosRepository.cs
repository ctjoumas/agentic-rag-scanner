using Microsoft.Azure.Cosmos;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Marker contract for documents stored via <see cref="ICosmosRepository{T}"/>. Cosmos requires every
/// item to expose an <c>id</c>; implementers map this to the Cosmos <c>id</c> field (e.g. with the
/// Newtonsoft <c>[JsonProperty("id")]</c> attribute the SDK's serializer honors).
/// </summary>
public interface ICosmosEntity
{
    /// <summary>The document's unique id within its partition.</summary>
    string Id { get; }
}

/// <summary>
/// Generic CRUD repository over the configured Cosmos DB container
/// (<see cref="AgenticRagScannerApi.Workflows.Configuration.CosmosOptions.RegDocsContainer"/>). The
/// partition key value is supplied explicitly by the caller so the repository stays agnostic of each
/// document's partitioning scheme.
/// </summary>
/// <typeparam name="T">The document type; must expose an id via <see cref="ICosmosEntity"/>.</typeparam>
public interface ICosmosRepository<T>
    where T : class, ICosmosEntity
{
    /// <summary>Creates a new document. Throws if an item with the same id already exists in the partition.</summary>
    Task<T> CreateAsync(T item, string partitionKey, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a document (idempotent upsert).</summary>
    Task<T> UpsertAsync(T item, string partitionKey, CancellationToken cancellationToken = default);

    /// <summary>Reads a document by id and partition key; returns <see langword="null"/> if it does not exist.</summary>
    Task<T?> ReadAsync(string id, string partitionKey, CancellationToken cancellationToken = default);

    /// <summary>Replaces an existing document. Throws if the document does not exist.</summary>
    Task<T> ReplaceAsync(T item, string partitionKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes a document by id and partition key. Throws if the document does not exist.</summary>
    Task DeleteAsync(string id, string partitionKey, CancellationToken cancellationToken = default);

    /// <summary>Runs a query and returns all matching documents, optionally scoped to a single partition.</summary>
    Task<IReadOnlyList<T>> QueryAsync(QueryDefinition query, string? partitionKey = null, CancellationToken cancellationToken = default);
}
