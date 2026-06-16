using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Filters;
using AgenticRagScannerApi.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Optional local overrides (real secrets live here and are git-ignored).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Configuration (Options pattern) — bind each service's settings section.
builder.Services.Configure<AzureStorageOptions>(builder.Configuration.GetSection(AzureStorageOptions.SectionName));
builder.Services.Configure<AzureSearchOptions>(builder.Configuration.GetSection(AzureSearchOptions.SectionName));
builder.Services.Configure<FoundryOptions>(builder.Configuration.GetSection(FoundryOptions.SectionName));
builder.Services.Configure<BingSearchGroundingOptions>(builder.Configuration.GetSection(BingSearchGroundingOptions.SectionName));
builder.Services.Configure<BingCustomSearchGroundingOptions>(builder.Configuration.GetSection(BingCustomSearchGroundingOptions.SectionName));

// Service layer — one registration per external service.
// Singleton: these wrap thread-safe Azure SDK clients meant to be long-lived.
builder.Services.AddSingleton<IAzureStorageService, AzureStorageService>();
builder.Services.AddSingleton<IAzureSearchService, AzureSearchService>();
builder.Services.AddSingleton<IFoundryService, FoundryService>();
builder.Services.AddSingleton<IBingSearchGroundingService, BingSearchGroundingService>();
builder.Services.AddSingleton<IBingCustomSearchGroundingService, BingCustomSearchGroundingService>();

// Add services to the container.
builder.Services.AddControllers(options => options.Filters.Add<ApiExceptionFilterAttribute>());
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Interactive API reference UI (renders the OpenAPI doc at /openapi/v1.json).
    // Browse it at /scalar/v1.
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
