using AgenticRagScannerApi.Extensions;
using AgenticRagScannerApi.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Optional local overrides (real secrets live here and are git-ignored).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

WebApplicationBuilderExtensions.AddSerilogLogging(builder);

builder.Services.AddApiServices(builder.Configuration);

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
