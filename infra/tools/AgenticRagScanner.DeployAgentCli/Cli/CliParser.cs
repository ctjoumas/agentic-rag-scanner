using System.CommandLine;
using System.CommandLine.Invocation;
using AgenticRagScanner.DeployAgentCli.Runner;

namespace AgenticRagScanner.DeployAgentCli.Cli;

internal static class CliParser
{
    public static int Invoke(string[] args)
    {
        return BuildRootCommand().Invoke(args);
    }

    private static RootCommand BuildRootCommand()
    {
        var endpointOption = new Option<string?>("--endpoint", () => Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT"));
        var outputFormatOption = new Option<string>("--output-format", () => "human");
        outputFormatOption.FromAmong("human", "json");

        var logLevelOption = new Option<string>("--log-level", () => "information");
        logLevelOption.FromAmong("error", "warning", "information", "verbose");

        var root = new RootCommand("Deploy, list, or delete Foundry prompt agents.");

        var deploy = new Command("deploy", "Create or update prompt agent");
        var yamlPathOption = new Option<string?>("--yaml-path", () => null);
        var agentNameOption = new Option<string?>("--agent-name", () => null);
        var modelOption = new Option<string?>("--model", () => Environment.GetEnvironmentVariable("FOUNDRY_MODEL"));
        var webSearchConnectionIdOption = new Option<string?>(
            aliases: ["--web-search-connection-id", "--bing-custom-search-connection-id"],
            getDefaultValue: () =>
                Environment.GetEnvironmentVariable("FOUNDRY_BING_CONNECTION_ID") ??
                Environment.GetEnvironmentVariable("FOUNDRY_WEB_SEARCH_CONNECTION_ID"));
        var webSearchConnectionNameOption = new Option<string?>(
            aliases: ["--web-search-connection-name", "--bing-custom-search-connection-name"],
            getDefaultValue: () =>
                Environment.GetEnvironmentVariable("FOUNDRY_BING_CONNECTION_NAME") ??
                Environment.GetEnvironmentVariable("FOUNDRY_WEB_SEARCH_CONNECTION_NAME"));
        var webSearchInstanceNameOption = new Option<string?>(
            aliases: ["--web-search-instance-name", "--bing-custom-search-instance-name"],
            getDefaultValue: () =>
                Environment.GetEnvironmentVariable("FOUNDRY_BING_INSTANCE_NAME") ??
                Environment.GetEnvironmentVariable("FOUNDRY_WEB_SEARCH_INSTANCE_NAME"));

        deploy.AddOption(endpointOption);
        deploy.AddOption(outputFormatOption);
        deploy.AddOption(logLevelOption);
        deploy.AddOption(yamlPathOption);
        deploy.AddOption(agentNameOption);
        deploy.AddOption(modelOption);
        deploy.AddOption(webSearchConnectionIdOption);
        deploy.AddOption(webSearchConnectionNameOption);
        deploy.AddOption(webSearchInstanceNameOption);

        deploy.SetHandler((InvocationContext context) =>
        {
            var endpoint = context.ParseResult.GetValueForOption(endpointOption);
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                context.Console.Error.Write("Foundry project endpoint is required. Use --endpoint or set FOUNDRY_PROJECT_ENDPOINT.\n");
                context.ExitCode = 1;
                return;
            }

            var options = new DeployAgentOptions
            {
                Command = "deploy",
                Endpoint = endpoint,
                OutputFormat = context.ParseResult.GetValueForOption(outputFormatOption) ?? "human",
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "information",
                YamlPath = context.ParseResult.GetValueForOption(yamlPathOption),
                AgentName = context.ParseResult.GetValueForOption(agentNameOption),
                Model = context.ParseResult.GetValueForOption(modelOption),
                WebSearchConnectionId = context.ParseResult.GetValueForOption(webSearchConnectionIdOption),
                WebSearchConnectionName = context.ParseResult.GetValueForOption(webSearchConnectionNameOption),
                WebSearchInstanceName = context.ParseResult.GetValueForOption(webSearchInstanceNameOption),
            };

            context.ExitCode = DeployAgentRunner.Run(options);
        });

        var list = new Command("list", "List project agents");
        list.AddOption(endpointOption);
        list.AddOption(outputFormatOption);
        list.AddOption(logLevelOption);
        list.SetHandler((InvocationContext context) =>
        {
            var endpoint = context.ParseResult.GetValueForOption(endpointOption);
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                context.Console.Error.Write("Foundry project endpoint is required. Use --endpoint or set FOUNDRY_PROJECT_ENDPOINT.\n");
                context.ExitCode = 1;
                return;
            }

            var options = new DeployAgentOptions
            {
                Command = "list",
                Endpoint = endpoint,
                OutputFormat = context.ParseResult.GetValueForOption(outputFormatOption) ?? "human",
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "information",
            };
            context.ExitCode = DeployAgentRunner.Run(options);
        });

        var delete = new Command("delete", "Delete an agent by name");
        var agentNameArgument = new Argument<string>("agent-name", "Name of agent to delete");
        delete.AddArgument(agentNameArgument);
        delete.AddOption(endpointOption);
        delete.AddOption(outputFormatOption);
        delete.AddOption(logLevelOption);

        delete.SetHandler((InvocationContext context) =>
        {
            var endpoint = context.ParseResult.GetValueForOption(endpointOption);
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                context.Console.Error.Write("Foundry project endpoint is required. Use --endpoint or set FOUNDRY_PROJECT_ENDPOINT.\n");
                context.ExitCode = 1;
                return;
            }

            var options = new DeployAgentOptions
            {
                Command = "delete",
                Endpoint = endpoint,
                OutputFormat = context.ParseResult.GetValueForOption(outputFormatOption) ?? "human",
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption) ?? "information",
                DeleteAgentName = context.ParseResult.GetValueForArgument(agentNameArgument),
            };
            context.ExitCode = DeployAgentRunner.Run(options);
        });

        root.AddCommand(deploy);
        root.AddCommand(list);
        root.AddCommand(delete);
        return root;
    }
}
