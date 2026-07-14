namespace AgenticRagScannerApi.Workflows.CompanyView;

using AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// Retrieves prior Company View records for a jurisdiction, to ground the Company View agent (Epic 8,
/// story 8.5) via RAG. Records are shaped to match the historical <c>RegulatoryUpdatesCsv</c>
/// (<see cref="CompanyViewRecord"/>) so they serve as full house-style exemplars. The abstraction lives
/// in the Workflows project so the agent depends only on it; the retrieval SOURCE is swappable behind
/// this seam - a CSV of historical records for local testing, and a relational (SQL) source keyed by
/// jurisdiction in production (deferred). Mirrors how <see cref="Pipeline.IFullTextStore"/> is defined
/// here and implemented in the API host.
/// </summary>
public interface IPriorCompanyViewSource
{
    /// <summary>
    /// Returns prior Company View records for <paramref name="jurisdiction"/> (e.g. "United Kingdom"),
    /// or an empty list when the source is unavailable or has none for that jurisdiction. Matching is by
    /// exact jurisdiction, case-insensitively (the production query is
    /// <c>WHERE jurisdiction = @jurisdiction</c>).
    /// </summary>
    Task<IReadOnlyList<CompanyViewRecord>> GetByJurisdictionAsync(string jurisdiction, CancellationToken cancellationToken = default);
}
