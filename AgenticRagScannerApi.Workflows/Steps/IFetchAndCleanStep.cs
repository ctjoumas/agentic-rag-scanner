using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.Pipeline;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Full-text fetch &amp; clean step (scaffold for Epic 2): turns a <see cref="SearchHit"/> into a
/// <see cref="FetchedDocument"/>. The real step (Epic 5) fetches HTML/PDF, strips boilerplate, and on
/// failure falls back to the Bing snippet with <see cref="FetchedDocument.Unverified"/> set - it
/// never drops a document.
/// </summary>
public interface IFetchAndCleanStep
{
    /// <summary>Fetches and cleans the document for <paramref name="hit"/> (never returns null).</summary>
    Task<FetchedDocument> FetchAsync(SearchHit hit, CancellationToken cancellationToken = default);
}
