using AgenticRagScannerApi.Services;

namespace AgenticRagScannerApi.Seeding;

/// <summary>
/// One-off seeder that provisions the employment-taxes impact areas into Cosmos DB as documents with
/// <c>doc_type = "ImpactAreas"</c>. Run it with <c>dotnet run -- seed</c> from the API project; it
/// reuses the app's configuration and DI (Cosmos endpoint, database, RegDocs container, keyless auth)
/// and then exits without starting the web host. Each document gets a fresh GUID id, so re-running
/// creates a new set of documents rather than overwriting the previous run.
/// </summary>
/// <remarks>
/// The RegDocs container must already exist (provisioned via IaC) - keyless data-plane access does not
/// create it.
/// </remarks>
internal static class ImpactAreaSeeder
{
    /// <summary>Partition key value shared by every impact-area document (the container partitions on /doc_type).</summary>
    public const string ImpactAreaDocType = "ImpactAreas";

    private static readonly string[] ImpactAreas =
    [
        "Administration of employment taxes withholding & payments",
        "Employer tax reporting/filing requirements",
        "Taxation of equity & incentives",
        "Taxation of fringe benefits and employee expenses",
        "International/expat tax arrangements",
        "Employment taxes authority enforcement procedures",
        "Employment taxes rates & thresholds",
        "Employment taxes impact of termination/severance pay & benefits",
        "Employment taxes updates related to state of emergency",
        "Taxation of contractors and contingent labour",
        "Governance and controls of employment taxes requirements",
    ];

    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICosmosRepository<ImpactAreaDocument>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ImpactAreaSeeder));

        logger.LogInformation("Seeding {Count} impact-area documents (doc_type '{DocType}')...", ImpactAreas.Length, ImpactAreaDocType);

        foreach (var impactArea in ImpactAreas)
        {
            var document = new ImpactAreaDocument
            {
                Name = impactArea,
            };

            await repository.UpsertAsync(document, ImpactAreaDocType, cancellationToken);
        }

        logger.LogInformation("Seeded {Count} impact-area documents.", ImpactAreas.Length);
    }
}
