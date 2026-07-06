using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 8.5 - <see cref="CsvPriorDeloitteViewSource"/> parses the customer's historical regulatory-updates
/// CSV (with its description row under the header and multi-line quoted cells), filters prior Deloitte
/// Views by jurisdiction (case-insensitively), and degrades to empty when the file is missing/unset.
/// </summary>
public sealed class CsvPriorDeloitteViewSourceTests : IDisposable
{
    private const string Csv =
        "Jurisdiction,Impact Area,Tags,Title of Update,Summary of Update,Deloitte View,Level of Authority,Status of Change,Announcement date,Effective Date of Change,Supporting reference,Regulator\n" +
        "INTERNAL USE ONLY,description,description,description,description,,description,description,description,description,description,description\n" +
        "United Kingdom,Employment taxes rates & thresholds,\"Fuel Rates, Company Cars\",Advisory Fuel Rates,AFR summary,\"Employers should update expenses policies.\nStay alert to non-routine updates.\",Regulator guidance,In force,2026-03-01,2026-03-01,https://gov.uk/afr,HMRC\n" +
        "Australia,Some area,Payroll,Aussie update,Aussie summary,Australian view text.,Legislation,Proposed,2026-01-01,2026-07-01,https://ato.gov.au/x,ATO\n" +
        "United Kingdom,Governance,IR35,Second UK update,Second summary,Second UK view.,Regulator guidance,In force,2026-02-01,2026-04-06,https://gov.uk/ir35,HMRC\n";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"regupdates-{Guid.NewGuid():N}.csv");

    private CsvPriorDeloitteViewSource CreateSource(string? filePath)
    {
        var options = Options.Create(new RegulatoryUpdatesCsvOptions { FilePath = filePath ?? string.Empty });
        return new CsvPriorDeloitteViewSource(options, NullLogger<CsvPriorDeloitteViewSource>.Instance);
    }

    [Fact]
    public async Task GetByJurisdictionAsync_ReturnsMatchingRows_AndSkipsDescriptionRow()
    {
        File.WriteAllText(_path, Csv);
        var source = CreateSource(_path);

        var views = await source.GetByJurisdictionAsync("United Kingdom");

        views.Should().HaveCount(2);
        views.Select(v => v.TitleOfUpdate).Should().BeEquivalentTo(new[] { "Advisory Fuel Rates", "Second UK update" });
        views.Should().Contain(v => v.DeloitteView!.Contains("Employers should update expenses policies")
            && v.DeloitteView.Contains("Stay alert to non-routine updates."));

        var afr = views.Single(v => v.TitleOfUpdate == "Advisory Fuel Rates");
        afr.Jurisdiction.Should().Be("United Kingdom");
        afr.ImpactArea.Should().Be("Employment taxes rates & thresholds");
        afr.Tags.Should().BeEquivalentTo(new[] { "Fuel Rates", "Company Cars" });
        afr.LevelOfAuthority.Should().Be("Regulator guidance");
        afr.StatusOfChange.Should().Be("In force");
        afr.AnnouncementDate.Should().Be("2026-03-01");
        afr.EffectiveDateOfChange.Should().Be("2026-03-01");
        afr.SupportingReference.Should().Be("https://gov.uk/afr");
        afr.Regulator.Should().Be("HMRC");
    }

    [Fact]
    public async Task GetByJurisdictionAsync_IsCaseInsensitive()
    {
        File.WriteAllText(_path, Csv);
        var source = CreateSource(_path);

        var views = await source.GetByJurisdictionAsync("united kingdom");

        views.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByJurisdictionAsync_ReturnsEmpty_ForUnknownJurisdiction()
    {
        File.WriteAllText(_path, Csv);
        var source = CreateSource(_path);

        var views = await source.GetByJurisdictionAsync("Narnia");

        views.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByJurisdictionAsync_ReturnsEmpty_WhenFileMissing()
    {
        var source = CreateSource(Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.csv"));

        var views = await source.GetByJurisdictionAsync("United Kingdom");

        views.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByJurisdictionAsync_ReturnsEmpty_WhenPathNotConfigured()
    {
        var source = CreateSource(filePath: null);

        var views = await source.GetByJurisdictionAsync("United Kingdom");

        views.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByJurisdictionAsync_CachesFile_AndSurvivesDeletion()
    {
        File.WriteAllText(_path, Csv);
        var source = CreateSource(_path);

        var first = await source.GetByJurisdictionAsync("Australia");
        File.Delete(_path);
        var second = await source.GetByJurisdictionAsync("Australia");

        first.Should().HaveCount(1);
        second.Should().HaveCount(1);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
