using AgenticRagScannerApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Optional local overrides (real secrets live here and are git-ignored).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

WebApplicationBuilderExtensions.AddSerilogLogging(builder);

builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseApiMiddleware();

app.Run();
