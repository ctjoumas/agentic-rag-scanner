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
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using FluentValidation;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;
using System.ClientModel;

namespace AgenticRagScannerApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .AddConfiguredOptions(configuration)
            .AddAzureSdkClients()
            .AddCoreServices()
            .AddFoundryChatClient()
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
        services.AddOptions<QuerySynthesisOptions>().Bind(configuration.GetSection(QuerySynthesisOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<CosmosOptions>().Bind(configuration.GetSection(CosmosOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<WebSearchOptions>().Bind(configuration.GetSection(WebSearchOptions.SectionName)).ValidateDataAnnotations();
        services.AddOptions<FetchOptions>().Bind(configuration.GetSection(FetchOptions.SectionName)).ValidateDataAnnotations();

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
        services.AddSingleton<IScanMapper, ScanMapper>();

        // Shared throttle - Phase 0 pass-through; real TPM/RPM/QPS limits arrive later.
        services.AddSingleton<ISharedThrottle, NoOpThrottle>();

        // Keyless auth - inject this TokenCredential into Azure SDK clients (keys are local-dev only).
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        return services;
    }

    public static IServiceCollection AddFoundryChatClient(this IServiceCollection services)
    {
        // The single Foundry chat client (Microsoft.Extensions.AI IChatClient) that every MAF agent
        // references (Epic 3, story 3.1). Built directly against the Foundry model deployment - keyless
        // via DefaultAzureCredential, with an API key honored for local dev. Wrapped with a Polly
        // resilience pipeline + the shared throttle (ResilientChatClient) and OpenTelemetry GenAI
        // instrumentation so token/latency metrics are emitted once an exporter is wired (story 0.5).
        services.AddSingleton<IChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;

            var azureClient = string.IsNullOrWhiteSpace(options.ApiKey)
                ? new AzureOpenAIClient(new Uri(options.Endpoint), sp.GetRequiredService<TokenCredential>())
                : new AzureOpenAIClient(new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey));

            IChatClient inner = azureClient
                .GetChatClient(options.ModelDeploymentName)
                .AsIChatClient();

            IChatClient resilient = new ResilientChatClient(
                inner,
                sp.GetRequiredService<ISharedThrottle>(),
                options,
                sp.GetRequiredService<ILogger<ResilientChatClient>>());

            return new ChatClientBuilder(resilient)
                .UseOpenTelemetry()
                .Build(sp);
        });

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
        // MAF workflow agents over the shared Foundry IChatClient. Query Synthesis (Epic 3) and
        // Relevance Eval (Epic 6) are real; the remaining three stay stubs (no LLM calls) until Epic 7.
        services.AddSingleton<IQuerySynthesisAgent, QuerySynthesisAgent>();
        services.AddSingleton<IRelevanceEvalAgent, RelevanceEvalAgent>();
        services.AddSingleton<IEnrichmentAgent, EnrichmentAgentStub>();
        services.AddSingleton<ICategorizeAgent, CategorizeAgentStub>();
        services.AddSingleton<ISummarizeImpactAgent, SummarizeImpactAgentStub>();

        // Deterministic steps + the allowlist-gated web search agent (canned hits in Epic 2).
        services.AddSingleton<IPreFilterStep, PreFilterStep>();
        services.AddSingleton<IFetchAndCleanStep, FetchAndCleanStep>();
        services.AddSingleton<IFullTextStore, FullTextStore>();
        services.AddSingleton<ILoopController, LoopController>();
        services.AddSingleton<IVerdictRouting, VerdictRouting>();

        // Named HttpClient backing the Fetch & clean step (Epic 5, story 5.2). Auto-decompress and cap
        // redirects per FetchOptions; the per-fetch timeout is enforced in the step (handler timeout
        // disabled so it does not pre-empt the linked CancellationToken).
        services.AddHttpClient(FetchAndCleanStep.HttpClientName, (sp, client) =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AgenticRagScanner/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<FetchOptions>>().Value;
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = options.MaxRedirects > 0,
                    MaxAutomaticRedirections = Math.Max(1, options.MaxRedirects),
                    AutomaticDecompression = System.Net.DecompressionMethods.All,
                };
            });

        // Web Search agent (Epic 4, story 4.1): references the pre-provisioned hosted Foundry agent
        // (created in the portal with the Grounding with Bing Custom Search tool attached). The hosted
        // agent owns its model, instructions, and tools, so no client-side tool construction is needed -
        // we resolve it by name (latest version unless AgentVersion is pinned) and run it as a standard
        // MAF AIAgent. WebSearchAgent itself depends only on the MAF AIAgent abstraction.
        services.AddSingleton<IWebSearchAgent>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WebSearchOptions>>().Value;

            var projectClient = new AIProjectClient(
                new Uri(options.ProjectEndpoint),
                sp.GetRequiredService<TokenCredential>());

            AIAgent agent;
            if (string.IsNullOrWhiteSpace(options.AgentVersion))
            {
                ProjectsAgentRecord record = projectClient.AgentAdministrationClient.GetAgent(options.AgentName);
                agent = projectClient.AsAIAgent(record);
            }
            else
            {
                ProjectsAgentVersion version = projectClient.AgentAdministrationClient.GetAgentVersion(options.AgentName, options.AgentVersion);
                agent = projectClient.AsAIAgent(version);
            }

            // Retry transient Bing-grounding failures with exponential backoff + jitter and a per-attempt
            // timeout, mirroring ResilientChatClient. A single agent error still degrades gracefully:
            // once retries are exhausted the agent logs and returns no hits rather than aborting the run.
            var resilience = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds),
                    ShouldHandle = static args => ValueTask.FromResult(IsTransientWebSearchFailure(args.Outcome.Exception)),
                })
                .AddTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds))
                .Build();

            return new WebSearchAgent(
                agent,
                sp.GetRequiredService<IOptions<WebSearchOptions>>(),
                sp.GetRequiredService<ISharedThrottle>(),
                resilience,
                sp.GetRequiredService<ILogger<WebSearchAgent>>());
        });

        // The MAF Cosmos checkpoint store.
        services.AddSingleton<CosmosCheckpointStore>();

        return services;
    }

    /// <summary>
    /// Classifies an exception thrown by the hosted Web Search agent as transient (worth retrying).
    /// Matches the transient surface used by <see cref="ResilientChatClient"/>: connection drops,
    /// request timeouts, throttling (429), and server-side (5xx) failures.
    /// </summary>
    private static bool IsTransientWebSearchFailure(Exception? exception) => exception switch
    {
        ClientResultException clientResult => clientResult.Status is 0 or 408 or 429 or >= 500,
        RequestFailedException requestFailed => requestFailed.Status is 0 or 408 or 429 or >= 500,
        HttpRequestException => true,
        TimeoutException => true,
        _ => false,
    };

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