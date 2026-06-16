using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Filters;
using AgenticRagScannerApi.Mappers;
using AgenticRagScannerApi.Services;
using AgenticRagScannerApi.Validators;
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
        services.Configure<AzureStorageOptions>(configuration.GetSection(AzureStorageOptions.SectionName));
        services.Configure<AzureSearchOptions>(configuration.GetSection(AzureSearchOptions.SectionName));
        services.Configure<FoundryOptions>(configuration.GetSection(FoundryOptions.SectionName));
        services.Configure<BingSearchGroundingOptions>(configuration.GetSection(BingSearchGroundingOptions.SectionName));
        services.Configure<BingCustomSearchGroundingOptions>(configuration.GetSection(BingCustomSearchGroundingOptions.SectionName));

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

        return services;
    }
}