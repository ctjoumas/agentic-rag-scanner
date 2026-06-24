using System.Text.Json.Serialization;

namespace AgenticRagScannerApi.Core.Contracts;

/// <summary>
/// Source authority tier, stamped by the deterministic quality gate from the source domain
/// (horizon-scanner-architecture.md, step 15). Higher numeric value = higher authority, so items
/// can be ranked / tie-broken (legislation &gt; court ruling &gt; regulator guidance).
/// Serialized by name so API responses and checkpoints are human-readable; the string converter
/// still reads legacy integer values for backward compatibility. The explicit numeric values are
/// retained for in-code ranking.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LevelOfAuthority>))]
public enum LevelOfAuthority
{
    /// <summary>Source/domain could not be classified; lowest. Flag for review.</summary>
    Unknown = 0,

    /// <summary>Agency / regulator guidance - e.g. HMRC on gov.uk/hm-revenue-customs.</summary>
    RegulatorGuidance = 1,

    /// <summary>Court decision - e.g. supremecourt.uk.</summary>
    CourtRuling = 2,

    /// <summary>Statute / regulation - e.g. legislation.gov.uk (highest).</summary>
    Legislation = 3
}
