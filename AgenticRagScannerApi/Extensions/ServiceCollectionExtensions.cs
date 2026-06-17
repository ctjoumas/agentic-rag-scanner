using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Core.Throttling;
using AgenticRagScannerApi.Filters;
using AgenticRagScannerApi.Mappers;
using AgenticRagScannerApi.Services;
using AgenticRagScannerApi.Validators;
using Azure.Core;
using Azure.Identity;
using FluentValidation;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;

namespace AgenticRagScannerApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .AddConfiguredOptions(configuration)
            .AddCoreServices()
            .AddValidationServices()
            .AddApiFrameworkServices();
    }

    public static IServiceCollection AddConfiguredOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuration (Options pattern) — bind each service's settings section.
        services.AddOptions<AzureStorageOptions>().Bind(configuration.GetSection(AzureStorageOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<AzureSearchOptions>().Bind(configuration.GetSection(AzureSearchOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<FoundryOptions>().Bind(configuration.GetSection(FoundryOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<BingSearchGroundingOptions>().Bind(configuration.GetSection(BingSearchGroundingOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<BingCustomSearchGroundingOptions>().Bind(configuration.GetSection(BingCustomSearchGroundingOptions.SectionName)).ValidateDataAnnotations();

        return services;
    }

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {

        // Service layer — one registration per external service.
        // Singleton: these wrap thread-safe Azure SDK clients meant to be long-lived.
        services.AddSingleton<IAzureStorageService, AzureStorageService>();
        services.AddSingleton<IAzureSearchService, AzureSearchService>();
        services.AddSingleton<IFoundryService, FoundryService>();
        services.AddSingleton<IBingSearchGroundingService, BingSearchGroundingService>();
        services.AddSingleton<IBingCustomSearchGroundingService, BingCustomSearchGroundingService>();
        services.AddSingleton<IScanMapper, ScanMapper>();

        // Shared throttle - Phase 0 pass-through; real TPM/RPM/QPS limits arrive later.
        services.AddSingleton<ISharedThrottle, NoOpThrottle>();

        // Keyless auth - inject this TokenCredential into Azure SDK clients (keys are local-dev only).
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        return services;
    }

    public static IServiceCollection AddValidationServices(this IServiceCollection services)
    {

        // Validation layer — register FluentValidation and discover validators.
        services.AddFluentValidationAutoValidation(options =>
        {
            options.DisableBuiltInModelValidation = true;
        });
        services.AddValidatorsFromAssemblyContaining<ScanRequestValidator>();

        return services;
    }

    public static IServiceCollection AddApiFrameworkServices(this IServiceCollection services)
    {

        // Add services to the container.
        services.AddControllers(options => options.Filters.Add<ApiExceptionFilterAttribute>());
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        services.AddOpenApi();

        // Liveness endpoint (mapped at /health). Dependency readiness checks added later.
        services.AddHealthChecks();

        return services;
    }
}