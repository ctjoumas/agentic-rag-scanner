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
using AgenticRagScannerApi.Workflows.CompanyView;
using AgenticRagScannerApi.Workflows.Pipeline;
using AgenticRagScannerApi.Workflows.Steps;
using AgenticRagScannerApi.Workflows.Tools;
using AgenticRagScannerApi.Workflows.Vocabulary;
using Azure;
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
using System.ClientModel.Primitives;

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
        // Configuration (Options pattern) — bind each service's settings section. ValidateOnStart forces
        // data-annotation validation at application startup (fail fast) instead of lazily on first use,
        // so a misconfigured section (e.g. a placeholder ProjectEndpoint left in appsettings.json because
        // appsettings.Local.json was not loaded) surfaces immediately at boot with the offending field.
        services.AddOptions<AzureStorageOptions>().Bind(configuration.GetSection(AzureStorageOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AzureSearchOptions>().Bind(configuration.GetSection(AzureSearchOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<FoundryOptions>().Bind(configuration.GetSection(FoundryOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<QuerySynthesisOptions>().Bind(configuration.GetSection(QuerySynthesisOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CompanyViewOptions>().Bind(configuration.GetSection(CompanyViewOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CosmosOptions>().Bind(configuration.GetSection(CosmosOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<WebSearchOptions>().Bind(configuration.GetSection(WebSearchOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<FetchOptions>().Bind(configuration.GetSection(FetchOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RegulatoryUpdatesCsvOptions>().Bind(configuration.GetSection(RegulatoryUpdatesCsvOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

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

        // Generic Cosmos DB CRUD repository over the RegDocs container (reuses the CosmosClient singleton).
        services.AddSingleton(typeof(ICosmosRepository<>), typeof(CosmosRepository<>));

        // Categorization vocabularies (impact areas + tags) read from Cosmos RegDocs at runtime (Epic 8,
        // story 8.6). Singleton so the small, static vocabularies are loaded once and cached.
        services.AddSingleton<IRegulatoryVocabularyProvider, CosmosVocabularyProvider>();

        // Prior Company Views source for the Company View agent (Epic 8, story 8.5). CSV-backed for
        // local testing (SQL in production, behind the same abstraction); singleton so the file is parsed
        // once and cached.
        services.AddSingleton<IPriorCompanyViewSource, CsvPriorCompanyViewSource>();

        // Shared throttle - Phase 0 pass-through; real TPM/RPM/QPS limits arrive later.
        services.AddSingleton<ISharedThrottle, NoOpThrottle>();

        // Keyless auth - inject this TokenCredential into Azure SDK clients (keys are local-dev only).
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        return services;
    }

    public static IServiceCollection AddFoundryChatClient(this IServiceCollection services)
    {
        // The Foundry chat client factory (Epic 3, story 3.1). Every MAF agent shares the same Foundry
        // endpoint; only the model deployment differs, so agents can be pinned to different models via
        // Foundry:Agents:<AgentName>:ModelDeploymentName. The factory builds one resilient IChatClient per
        // deployment - keyless via DefaultAzureCredential (an API key is honored for local dev), wrapped
        // with a Polly resilience pipeline + the shared throttle (ResilientChatClient) and OpenTelemetry
        // GenAI instrumentation - and caches them.
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        // The default IChatClient (the shared model deployment) for non-agent consumers such as
        // IFoundryService and any agent without a per-agent override.
        services.AddSingleton<IChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Foundry.DefaultChatClient");
            logger.LogInformation(
                "Default Foundry chat client uses model deployment '{ModelDeploymentName}' (shared default for non-agent consumers and any agent without an override).",
                options.ModelDeploymentName);
            return sp.GetRequiredService<IChatClientFactory>().Create(options.ModelDeploymentName);
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
        // MAF workflow agents over the Foundry chat client factory. Query Synthesis (Epic 3), Relevance
        // Eval (Epic 6), Impact Area + Tags + Summary (Epic 8) are real; Enrichment stays a stub (parked).
        // Each real agent resolves its own model deployment (Foundry:Agents:<AgentName>:ModelDeploymentName,
        // falling back to the shared Foundry:ModelDeploymentName) so agents can run on different models
        // while sharing the same Foundry project.
        services.AddSingleton<IQuerySynthesisAgent>(sp =>
        {
            var foundry = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<QuerySynthesisAgent>>();
            var model = foundry.ResolveModel(FoundryAgentKeys.QuerySynthesis);
            logger.LogInformation("Agent '{Agent}' resolved to Foundry model deployment '{ModelDeploymentName}'.", FoundryAgentKeys.QuerySynthesis, model);
            var chatClient = sp.GetRequiredService<IChatClientFactory>().Create(model);
            return new QuerySynthesisAgent(
                chatClient,
                sp.GetRequiredService<IOptions<QuerySynthesisOptions>>(),
                logger);
        });
        services.AddSingleton<IRelevanceEvalAgent>(sp =>
        {
            var foundry = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<RelevanceEvalAgent>>();
            var model = foundry.ResolveModel(FoundryAgentKeys.RelevanceEval);
            logger.LogInformation("Agent '{Agent}' resolved to Foundry model deployment '{ModelDeploymentName}'.", FoundryAgentKeys.RelevanceEval, model);
            var chatClient = sp.GetRequiredService<IChatClientFactory>().Create(model);
            return new RelevanceEvalAgent(
                chatClient,
                logger);
        });
        services.AddSingleton<IEnrichmentAgent, EnrichmentAgentStub>();
        services.AddSingleton<IImpactAreaAgent>(sp =>
        {
            var foundry = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<ImpactAreaAgent>>();
            var model = foundry.ResolveModel(FoundryAgentKeys.ImpactArea);
            logger.LogInformation("Agent '{Agent}' resolved to Foundry model deployment '{ModelDeploymentName}'.", FoundryAgentKeys.ImpactArea, model);
            var chatClient = sp.GetRequiredService<IChatClientFactory>().Create(model);
            return new ImpactAreaAgent(
                chatClient,
                sp.GetRequiredService<IRegulatoryVocabularyProvider>(),
                logger);
        });
        services.AddSingleton<ITagsAgent>(sp =>
        {
            var foundry = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<TagsAgent>>();
            var model = foundry.ResolveModel(FoundryAgentKeys.Tags);
            logger.LogInformation("Agent '{Agent}' resolved to Foundry model deployment '{ModelDeploymentName}'.", FoundryAgentKeys.Tags, model);
            var chatClient = sp.GetRequiredService<IChatClientFactory>().Create(model);
            return new TagsAgent(
                chatClient,
                sp.GetRequiredService<IRegulatoryVocabularyProvider>(),
                logger);
        });
        services.AddSingleton<ICompanyViewAgent>(sp =>
        {
            var foundry = sp.GetRequiredService<IOptions<FoundryOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<CompanyViewAgent>>();
            var model = foundry.ResolveModel(FoundryAgentKeys.CompanyView);
            logger.LogInformation("Agent '{Agent}' resolved to Foundry model deployment '{ModelDeploymentName}'.", FoundryAgentKeys.CompanyView, model);
            var chatClient = sp.GetRequiredService<IChatClientFactory>().Create(model);
            return new CompanyViewAgent(
                chatClient,
                sp.GetRequiredService<IOptions<CompanyViewOptions>>(),
                logger);
        });

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

            // Raise the SDK network timeout above the default 100s (Bing-grounded agent runs can exceed
            // it) and disable the SDK's built-in retry so it does not compound with the Polly pipeline
            // below - the resilience pipeline owns retries.
            var projectOptions = new AIProjectClientOptions
            {
                NetworkTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            };

            var projectClient = new AIProjectClient(
                new Uri(options.ProjectEndpoint),
                sp.GetRequiredService<TokenCredential>(),
                projectOptions);

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
                .AddTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds + 30))
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
    /// Classifies an exception thrown by the hosted Web Search agent as transient (worth retrying):
    /// connection drops (status 0), request timeouts (408), throttling (429), and server-side (5xx)
    /// failures.
    /// <para>
    /// HTTP 408 is treated as transient (like <see cref="ResilientChatClient"/>). For a Bing-grounded
    /// agent a 408 means the run did not finish in time, which is often intermittent (Bing latency
    /// spikes, cold routing) rather than a permanent failure. The resilience pipeline retries with
    /// exponential backoff + jitter and a per-attempt timeout, giving the search a reasonable window to
    /// complete before the group is surfaced as Failed.
    /// </para>
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