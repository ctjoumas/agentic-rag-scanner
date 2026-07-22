using AgenticRagScannerApi.Extensions;
using AgenticRagScannerApi.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Optional local overrides (real secrets live here and are git-ignored). Anchored to the content root so
// it loads regardless of the process working directory, and added last so it wins over appsettings.json
// and appsettings.{Environment}.json. If a real endpoint is left as a placeholder here (or the file is
// missing), the ValidateOnStart data-annotation checks in AddConfiguredOptions fail fast at startup.
var localSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json");
builder.Configuration.AddJsonFile(localSettingsPath, optional: true, reloadOnChange: true);

WebApplicationBuilderExtensions.AddSerilogLogging(builder);

builder.Services.AddApiServices(builder.Configuration);

// OpenTelemetry traces + metrics for the parallel scan orchestrator (Epic 13.2). Exports to Azure
// Monitor when the App Insights connection string is set; otherwise the instruments are collected but not
// exported (no console exporter - it would flood the terminal and bury the Serilog logs).
builder.Services.AddScannerObservability(builder.Configuration);

var app = builder.Build();

// One-off data seeding: `dotnet run -- seed [tags|impactareas]` provisions the requested seed
// documents and exits (no web host). Omitting the scope runs every seeder.
if (args.Contains("seed", StringComparer.OrdinalIgnoreCase))
{
    var seedAll = !args.Any(a => a.Equals("tags", StringComparison.OrdinalIgnoreCase)
        || a.Equals("impactareas", StringComparison.OrdinalIgnoreCase));

    if (seedAll || args.Contains("tags", StringComparer.OrdinalIgnoreCase))
    {
        await TagSeeder.RunAsync(app.Services);
    }

    if (seedAll || args.Contains("impactareas", StringComparer.OrdinalIgnoreCase))
    {
        await ImpactAreaSeeder.RunAsync(app.Services);
    }

    return;
}

app.UseApiMiddleware();

app.Run();
