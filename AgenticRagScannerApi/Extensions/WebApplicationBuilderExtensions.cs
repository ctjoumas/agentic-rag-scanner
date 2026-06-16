using Serilog;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;

namespace AgenticRagScannerApi.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();

            var appInsightsConnectionString = context.Configuration["ApplicationInsights:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
            {
                loggerConfiguration.WriteTo.ApplicationInsights(appInsightsConnectionString, new TraceTelemetryConverter());
            }
        });

        return builder;
    }
}