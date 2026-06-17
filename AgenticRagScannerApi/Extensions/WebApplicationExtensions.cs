using Scalar.AspNetCore;
using Serilog;

namespace AgenticRagScannerApi.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiMiddleware(this WebApplication app)
    {
        app
            .UseApiDocumentation()
            .UseApiObservability()
            .UseApiTransport()
            .UseApiSecurity()
            .MapApiEndpoints();

        return app;
    }

    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            // Interactive API reference UI (renders the OpenAPI doc at /openapi/v1.json).
            // Browse it at /scalar/v1.
            app.MapScalarApiReference();
        }

        return app;
    }

    public static WebApplication UseApiObservability(this WebApplication app)
    {

        app.UseSerilogRequestLogging();

        return app;
    }

    public static WebApplication UseApiTransport(this WebApplication app)
    {

        app.UseHttpsRedirection();

        return app;
    }

    public static WebApplication UseApiSecurity(this WebApplication app)
    {

        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapApiEndpoints(this WebApplication app)
    {

        app.MapHealthChecks("/health");

        app.MapControllers();

        return app;
    }
}