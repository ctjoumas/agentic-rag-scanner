using AgenticRagScannerApi.Services;

namespace AgenticRagScannerApi.Seeding;

/// <summary>
/// One-off seeder that provisions the tag taxonomy into Cosmos DB as documents with
/// <c>doc_type = "tags"</c>. Run it with <c>dotnet run -- seed</c> from the API project; it reuses the
/// app's configuration and DI (Cosmos endpoint, database, RegDocs container, keyless auth) and then
/// exits without starting the web host. Each document gets a fresh GUID id, so re-running creates a new
/// set of tag documents rather than overwriting the previous run.
/// </summary>
/// <remarks>
/// The RegDocs container must already exist (provisioned via IaC) - keyless data-plane access does not
/// create it.
/// </remarks>
internal static class TagSeeder
{
    /// <summary>Partition key value shared by every tag document (the container partitions on /doc_type).</summary>
    public const string TagDocType = "tags";

    private static readonly string[] Tags =
    [
        "Payroll Reporting",
        "National Insurance",
        "Social Security",
        "International Tax Treaties",
        "Remote Work",
        "PE",
        "Expat tax regime",
        "s.690",
        "Overseas Workday Relief",
        "Company Cars",
        "Evs",
        "Benefits In Kind",
        "ECOS",
        "Class 2 NIC",
        "CIS",
        "Construction",
        "Tax Authority Enforcement",
        "Employee Benefits",
        "Exemptions",
        "Eye Tests",
        "IR35",
        "Off Payroll",
        "End Client",
        "Remote Working",
        "Home Exemption",
        "Tax Return Deduction",
        "Pensions",
        "Salary Sacrifice",
        "Fuel Rates",
        "Income Tax",
    ];

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICosmosRepository<TagDocument>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(TagSeeder));

        logger.LogInformation("Seeding {Count} tag documents (doc_type '{DocType}')...", Tags.Length, TagDocType);

        foreach (var tag in Tags)
        {
            var document = new TagDocument
            {
                Name = tag,
            };

            await repository.UpsertAsync(document, TagDocType, cancellationToken);
        }

        logger.LogInformation("Seeded {Count} tag documents.", Tags.Length);
    }
}
