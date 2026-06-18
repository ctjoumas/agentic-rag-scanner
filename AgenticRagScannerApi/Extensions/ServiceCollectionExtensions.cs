using AgenticRagScannerApi.Configuration;
using AgenticRagScannerApi.Core.Throttling;
using AgenticRagScannerApi.Filters;
using AgenticRagScannerApi.Mappers;
using AgenticRagScannerApi.Orchestration;
using AgenticRagScannerApi.Serialization;
using AgenticRagScannerApi.Services;
using AgenticRagScannerApi.Validators;
using AgenticRagScannerApi.Workflows.Agents;
using AgenticRagScannerApi.Workflows.Checkpointing;
using AgenticRagScannerApi.Workflows.Configuration;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Steps;
using AgenticRagScannerApi.Workflows.Tools;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using FluentValidation;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;

namespace AgenticRagScannerApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .AddConfiguredOptions(configuration)
            .AddAzureSdkClients()
            .AddCoreServices()
            .AddWorkflowServices()
            .AddOrchestrationServices()
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
        services.AddOptions<CosmosOptions>().Bind(configuration.GetSection(CosmosOptions.SectionName)).ValidateDataAnnotations();

        return services;
    }

    public static IServiceCollection AddAzureSdkClients(this IServiceCollection services)
    {
        // Azure SDK clients — registered as singletons (thread-safe, long-lived) and
        // injected into the service layer. Prefer the shared TokenCredential (keyless);
        // a connection string / API key is honored for local development only.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value;

            return string.IsNullOrWhiteSpace(options.ConnectionString)
                ? new BlobServiceClient(new Uri(options.BlobServiceUri), sp.GetRequiredService<TokenCredential>())
                : new BlobServiceClient(options.ConnectionString);
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureSearchOptions>>().Value;
            var endpoint = new Uri(options.Endpoint);

            return string.IsNullOrWhiteSpace(options.ApiKey)
                ? new SearchClient(endpoint, options.IndexName, sp.GetRequiredService<TokenCredential>())
                : new SearchClient(endpoint, options.IndexName, new AzureKeyCredential(options.ApiKey));
        });

        // Cosmos DB client — keyless (DefaultAzureCredential); backs MAF workflow checkpointing
        // (Epic 2) and the result store (Epic 8).
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;

            return new CosmosClient(options.Endpoint, sp.GetRequiredService<TokenCredential>());
        });

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

    public static IServiceCollection AddOrchestrationServices(this IServiceCollection services)
    {
        // Run lifecycle — synchronous, sequential scan orchestration.
        // Scoped: per-request coordination; the per-group executor now runs the per-group MAF
        // workflow (Epic 2), replacing the Phase 1 stub.
        services.AddScoped<IScanOrchestrator, ScanOrchestrator>();
        services.AddScoped<ITopicGroupExecutor, WorkflowTopicGroupExecutor>();

        return services;
    }

    public static IServiceCollection AddWorkflowServices(this IServiceCollection services)
    {
        // MAF workflow scaffolding (Epic 2). Agents are stubs (no LLM calls yet); the real
        // Foundry-backed implementations arrive in Epics 3/6/7.
        services.AddSingleton<IQuerySynthesisAgent, QuerySynthesisAgentStub>();
        services.AddSingleton<IRelevanceEvalAgent, RelevanceEvalAgentStub>();
        services.AddSingleton<IEnrichmentAgent, EnrichmentAgentStub>();
        services.AddSingleton<ICategorizeAgent, CategorizeAgentStub>();
        services.AddSingleton<ISummarizeImpactAgent, SummarizeImpactAgentStub>();

        // Deterministic steps + the allowlist-gated Bing tool (canned hits in Epic 2).
        services.AddSingleton<IPreFilterStep, PreFilterStep>();
        services.AddSingleton<IFetchAndCleanStep, FetchAndCleanStep>();
        services.AddSingleton<ILoopController, LoopController>();
        services.AddSingleton<IVerdictRouting, VerdictRouting>();
        services.AddSingleton<IBingSearchTool, BingSearchTool>();

        // Loop composition + the MAF Cosmos checkpoint store.
        services.AddSingleton<TopicGroupPipeline>();
        services.AddSingleton<CosmosCheckpointStore>();

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
        services.AddControllers(options => options.Filters.Add<ApiExceptionFilterAttribute>())
            .AddJsonOptions(options =>
            {
                // Accept "yyyy-MM-dd" and tolerate full ISO date-times for DateOnly fields.
                options.JsonSerializerOptions.Converters.Add(new DateOnlyJsonConverter());
            });
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        services.AddOpenApi();

        // Liveness endpoint (mapped at /health). Dependency readiness checks added later.
        services.AddHealthChecks();

        return services;
    }
}