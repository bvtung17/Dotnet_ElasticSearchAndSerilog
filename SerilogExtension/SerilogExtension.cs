using Microsoft.AspNetCore.Http.Features;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Filters;
using Serilog.Sinks.Elasticsearch;
using SerilogExtension.Host.Api.Extensions;

namespace SerilogExtension.Host.Api.Extensions;

public static class SerilogExtension
{
    public static WebApplicationBuilder AddSerilog(
        this WebApplicationBuilder builder,
        IConfiguration configuration)
    {
        //var pathLogFile = Path.Combine(Directory.GetCurrentDirectory(), "Log", "log-balance-.txt");

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Error)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Infrastructure", LogEventLevel.Error)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ApplicationName", $"SerilogExtension - {Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}")
            .Enrich.WithCorrelationId()
            .Enrich.WithExceptionDetails()
            .Filter.ByExcluding(Matching.FromSource("Microsoft.AspNetCore.StaticFiles"))
            //.WriteTo.Logger(writeTo => writeTo.Filter
            //                    .ByIncludingOnly(e => e.Properties.ContainsKey("BalanceHandler") && AppUtils.GetEnv() != Environments.Production)
            //                    .WriteTo.File(pathLogFile, rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true))
            .WriteTo.Async(writeTo => writeTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri("http://localhost:9200"))
            {
                TypeName = null,
                AutoRegisterTemplate = true,
                IndexFormat = "test-index-{0:yyyy.MM}",
                BatchAction = ElasticOpType.Create
            }))

            .WriteTo.Async(writeTo => writeTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"))
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Host.UseSerilog(Log.Logger, true);

        return builder;
    }

    public static WebApplication UseSerilog(this WebApplication app)
    {
        app.UseSerilogRequestLogging(opts =>
        {
            opts.EnrichDiagnosticContext = EnrichFromRequest;
        });

        return app;
    }

    public static void EnrichFromRequest(
        IDiagnosticContext diagnosticContext,
        HttpContext httpContext)
    {
        var isHasAnyClaim = httpContext is not null &&
            httpContext.User is not null &&
            httpContext.User.Claims is not null;

        if (isHasAnyClaim)
        {
            var userClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type is "id");
            var adminClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type is "admId");
            var emailClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type is "email");

            if (userClaim is not null)
                diagnosticContext.Set("UserId", userClaim?.Value);

            if (adminClaim is not null)
                diagnosticContext.Set("AdminId", adminClaim?.Value);

            if (emailClaim is not null)
                diagnosticContext.Set("Email", emailClaim?.Value);
        }

        diagnosticContext.Set("ClientIP", httpContext?.Request?.Headers?["X-Forwarded-For"].FirstOrDefault());
        diagnosticContext.Set("UserAgent", httpContext?.Request?.Headers?["User-Agent"].FirstOrDefault());
        diagnosticContext.Set("Resource", httpContext?.GetMetricsCurrentResourceName());
    }

    public static string? GetMetricsCurrentResourceName(this HttpContext httpContext)
    {
        if (httpContext is null)
            throw new ArgumentNullException(nameof(httpContext));

        var endpoint = httpContext?.Features?.Get<IEndpointFeature>()?.Endpoint;

        return endpoint?.Metadata?.GetMetadata<EndpointNameMetadata>()?.EndpointName;
    }
}
