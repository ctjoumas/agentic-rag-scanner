namespace AgenticRagScannerApi.Workflows.Vocabulary;

/// <summary>
/// Supplies the approved categorization vocabularies (impact areas and tags) to the downstream
/// categorization agents (Epic 8, stories 8.2/8.3). Cosmos DB (the RegDocs container, seeded via
/// <c>dotnet run -- seed</c>) is the single source of truth, so the vocabularies are read at runtime
/// rather than hardcoded into the prompts. The abstraction lives in the Workflows project so the
/// agents depend only on it; the concrete implementation lives in the API host (over
/// <c>ICosmosRepository&lt;T&gt;</c>), mirroring how <see cref="Pipeline.IFullTextStore"/> is wired.
/// </summary>
public interface IRegulatoryVocabularyProvider
{
    /// <summary>
    /// Returns the approved impact-area vocabulary (RegDocs, <c>doc_type = "ImpactAreas"</c>) - the
    /// closed, single-label set the Impact Area agent must pick exactly one value from.
    /// </summary>
    Task<IReadOnlyList<string>> GetImpactAreasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the approved tag vocabulary (RegDocs, <c>doc_type = "tags"</c>) - the multi-label set
    /// the Tags agent may draw one or more values from.
    /// </summary>
    Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken = default);
}
