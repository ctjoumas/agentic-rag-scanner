using System.Globalization;
using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Core.Contracts;
using AgenticRagScannerApi.Workflows.CompanyView;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// CSV-backed <see cref="IPriorCompanyViewSource"/> (Epic 8, story 8.5): reads the customer's historical
/// regulatory-updates spreadsheet (exported to CSV, path from <see cref="RegulatoryUpdatesCsvOptions"/>)
/// and returns the prior Company Views for a jurisdiction. This is the local-testing source; production
/// swaps in a SQL-backed implementation behind the same abstraction. The file is parsed once and cached
/// (grouped by jurisdiction) since it is static for the process lifetime; a missing/empty path or file
/// degrades to an empty result rather than throwing, so the agent still runs (just without exemplars).
/// </summary>
public sealed class CsvPriorCompanyViewSource : IPriorCompanyViewSource
{
    private readonly RegulatoryUpdatesCsvOptions _options;
    private readonly ILogger<CsvPriorCompanyViewSource> _logger;

    private readonly object _gate = new();
    private IReadOnlyDictionary<string, IReadOnlyList<CompanyViewRecord>>? _byJurisdiction;

    public CsvPriorCompanyViewSource(IOptions<RegulatoryUpdatesCsvOptions> options, ILogger<CsvPriorCompanyViewSource> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<CompanyViewRecord>> GetByJurisdictionAsync(string jurisdiction, CancellationToken cancellationToken = default)
    {
        var index = GetOrLoadIndex();

        if (index.TryGetValue(jurisdiction.Trim(), out var views))
        {
            return Task.FromResult(views);
        }

        _logger.LogInformation("No prior Company Views found for jurisdiction '{Jurisdiction}' in the CSV source.", jurisdiction);
        return Task.FromResult<IReadOnlyList<CompanyViewRecord>>([]);
    }

    /// <summary>Loads and caches the CSV, grouped by jurisdiction (case-insensitive keys). Thread-safe.</summary>
    private IReadOnlyDictionary<string, IReadOnlyList<CompanyViewRecord>> GetOrLoadIndex()
    {
        if (_byJurisdiction is not null)
        {
            return _byJurisdiction;
        }

        lock (_gate)
        {
            _byJurisdiction ??= Load();
        }

        return _byJurisdiction;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<CompanyViewRecord>> Load()
    {
        if (string.IsNullOrWhiteSpace(_options.FilePath))
        {
            _logger.LogWarning("RegulatoryUpdatesCsv:FilePath is not configured; the Company View agent will run without prior-view exemplars.");
            return Empty();
        }

        if (!File.Exists(_options.FilePath))
        {
            _logger.LogWarning("Company Views CSV not found at '{FilePath}'; the Company View agent will run without prior-view exemplars.", _options.FilePath);
            return Empty();
        }

        try
        {
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                // The customer spreadsheet has a description row under the header and free-text cells; be
                // lenient so a single malformed cell never aborts the load.
                HeaderValidated = null,
                MissingFieldFound = null,
                BadDataFound = null,
                TrimOptions = TrimOptions.Trim,
            };

            using var reader = new StreamReader(_options.FilePath);
            using var csv = new CsvReader(reader, configuration);

            var index = new Dictionary<string, List<CompanyViewRecord>>(StringComparer.OrdinalIgnoreCase);
            var total = 0;

            foreach (var row in csv.GetRecords<CsvRow>())
            {
                var jurisdiction = row.Jurisdiction?.Trim();
                var view = row.CompanyView?.Trim();

                // Skip blank rows and the description row under the header (it has no real Company View).
                if (string.IsNullOrWhiteSpace(jurisdiction) || string.IsNullOrWhiteSpace(view))
                {
                    continue;
                }

                if (!index.TryGetValue(jurisdiction, out var list))
                {
                    list = [];
                    index[jurisdiction] = list;
                }

                list.Add(new CompanyViewRecord
                {
                    Jurisdiction = jurisdiction,
                    ImpactArea = row.ImpactArea?.Trim(),
                    Tags = SplitTags(row.Tags),
                    TitleOfUpdate = row.Title?.Trim(),
                    SummaryOfUpdate = row.Summary?.Trim(),
                    CompanyView = view,
                    LevelOfAuthority = row.LevelOfAuthority?.Trim(),
                    StatusOfChange = row.StatusOfChange?.Trim(),
                    AnnouncementDate = row.AnnouncementDate?.Trim(),
                    EffectiveDateOfChange = row.EffectiveDate?.Trim(),
                    SupportingReference = row.SupportingReference?.Trim(),
                    Regulator = row.Regulator?.Trim(),
                });
                total++;
            }

            _logger.LogInformation(
                "Loaded {Total} prior Company View(s) across {Jurisdictions} jurisdiction(s) from '{FilePath}'.",
                total, index.Count, _options.FilePath);

            return index.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<CompanyViewRecord>)kvp.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or CsvHelperException)
        {
            _logger.LogWarning(ex, "Failed to read the Company Views CSV at '{FilePath}'; continuing without prior-view exemplars.", _options.FilePath);
            return Empty();
        }
    }

    /// <summary>Splits the CSV's single comma-separated <c>Tags</c> cell into distinct, trimmed tags.</summary>
    private static IReadOnlyList<string> SplitTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        return tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CompanyViewRecord>> Empty() =>
        new Dictionary<string, IReadOnlyList<CompanyViewRecord>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps the customer CSV's columns (by header name) to the <see cref="CompanyViewRecord"/> fields.</summary>
    private sealed class CsvRow
    {
        [Name("Jurisdiction")]
        public string? Jurisdiction { get; set; }

        [Name("Impact Area")]
        public string? ImpactArea { get; set; }

        [Name("Tags")]
        public string? Tags { get; set; }

        [Name("Title of Update")]
        public string? Title { get; set; }

        [Name("Summary of Update")]
        public string? Summary { get; set; }

        [Name("Company View")]
        public string? CompanyView { get; set; }

        [Name("Level of Authority")]
        public string? LevelOfAuthority { get; set; }

        [Name("Status of Change")]
        public string? StatusOfChange { get; set; }

        [Name("Announcement date")]
        public string? AnnouncementDate { get; set; }

        [Name("Effective Date of Change")]
        public string? EffectiveDate { get; set; }

        [Name("Supporting reference")]
        public string? SupportingReference { get; set; }

        [Name("Regulator")]
        public string? Regulator { get; set; }
    }
}
