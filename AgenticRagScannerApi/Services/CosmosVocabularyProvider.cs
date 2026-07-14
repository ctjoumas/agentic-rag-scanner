using AgenticRagScannerApi.Seeding;
using AgenticRagScannerApi.Workflows.Vocabulary;
using Microsoft.Azure.Cosmos;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Cosmos-backed <see cref="IRegulatoryVocabularyProvider"/> (Epic 8, story 8.6): reads the approved
/// impact-area and tag vocabularies from the RegDocs container at runtime via
/// <see cref="ICosmosRepository{T}"/>, scoping each query to its <c>/doc_type</c> partition
/// (<see cref="ImpactAreaSeeder.ImpactAreaDocType"/> / <see cref="TagSeeder.TagDocType"/>) and
/// projecting the <c>name</c> field. Cosmos is the single source of truth - the lists are seeded via
/// <c>dotnet run -- seed</c> and must never be hardcoded into the agents/prompts.
/// </summary>
/// <remarks>
/// The vocabularies are small and effectively static for the lifetime of the process (they change only
/// when re-seeded, which requires a redeploy/restart to matter), so each list is loaded once and cached.
/// A per-partition <see cref="SemaphoreSlim"/> collapses concurrent first-reads into a single Cosmos
/// query. Registered as a singleton so the cache is shared.
/// </remarks>
public sealed class CosmosVocabularyProvider : IRegulatoryVocabularyProvider
{
    private readonly ICosmosRepository<VocabularyDocument> _repository;
    private readonly ILogger<CosmosVocabularyProvider> _logger;

    private readonly SemaphoreSlim _impactAreasGate = new(1, 1);
    private readonly SemaphoreSlim _tagsGate = new(1, 1);

    private IReadOnlyList<string>? _impactAreas;
    private IReadOnlyList<string>? _tags;

    public CosmosVocabularyProvider(ICosmosRepository<VocabularyDocument> repository, ILogger<CosmosVocabularyProvider> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> GetImpactAreasAsync(CancellationToken cancellationToken = default) =>
        GetVocabularyAsync(ImpactAreaSeeder.ImpactAreaDocType, _impactAreasGate, () => _impactAreas, value => _impactAreas = value, cancellationToken);

    public Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken = default) =>
        GetVocabularyAsync(TagSeeder.TagDocType, _tagsGate, () => _tags, value => _tags = value, cancellationToken);

    private async Task<IReadOnlyList<string>> GetVocabularyAsync(
        string docType,
        SemaphoreSlim gate,
        Func<IReadOnlyList<string>?> read,
        Action<IReadOnlyList<string>> store,
        CancellationToken cancellationToken)
    {
        var cached = read();
        if (cached is not null)
        {
            return cached;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = read();
            if (cached is not null)
            {
                return cached;
            }

            // Scope the query to the partition (doc_type); the repository deserializes each row into a
            // VocabularyDocument, so we read whole documents and project the name client-side. Ordering
            // keeps the vocabulary stable across reads (helpful for deterministic prompts and eval).
            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name");
            var documents = await _repository.QueryAsync(query, docType, cancellationToken).ConfigureAwait(false);

            var names = documents
                .Select(d => d.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                _logger.LogWarning(
                    "Vocabulary '{DocType}' is empty in the RegDocs container. Seed it with `dotnet run -- seed`.",
                    docType);
            }
            else
            {
                _logger.LogInformation("Loaded {Count} '{DocType}' vocabulary term(s) from Cosmos.", names.Count, docType);
            }

            store(names);
            return names;
        }
        finally
        {
            gate.Release();
        }
    }
}
