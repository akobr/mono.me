using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(
        this TBuilder @this,
        IEnumerable<Action<MeterProviderBuilder>>? meterConfigs = null,
        IEnumerable<Action<TracerProviderBuilder>>? tracerConfigs = null)
        where TBuilder : IHostApplicationBuilder
    {
        @this.ConfigureOpenTelemetry(meterConfigs, tracerConfigs);
        @this.AddBasicServiceDefaults();
        return @this;
    }

    public static TBuilder AddOrleansServiceDefaults<TBuilder>(
        this TBuilder @this,
        IEnumerable<Action<MeterProviderBuilder>>? meterConfigs = null,
        IEnumerable<Action<TracerProviderBuilder>>? tracerConfigs = null)
        where TBuilder : IHostApplicationBuilder
    {
        Action<MeterProviderBuilder>[] metrics =
        [
            metrics =>
            {
                metrics.AddMeter("Microsoft.Orleans");
                metrics.AddMeter("42.Crumble");
            }
        ];

        Action<TracerProviderBuilder>[] traces =
        [
            traces =>
            {
                traces.AddSource("Microsoft.Orleans.Runtime");
                traces.AddSource("Microsoft.Orleans.Application");
                traces.AddSource("42.Crumble");
            }
        ];

        @this.ConfigureOpenTelemetry(
            metrics.Concat(meterConfigs ?? []),
            traces.Concat(tracerConfigs ?? []));
        @this.AddBasicServiceDefaults();
        return @this;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(
        this TBuilder @this,
        IEnumerable<Action<MeterProviderBuilder>>? meterConfigs = null,
        IEnumerable<Action<TracerProviderBuilder>>? tracerConfigs = null)
        where TBuilder : IHostApplicationBuilder
    {
        meterConfigs ??= [];
        tracerConfigs ??= [];

        @this.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        @this.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                foreach (var config in meterConfigs)
                {
                    config(metrics);
                }
            })
            .WithTracing(tracing =>
            {
                foreach (var config in tracerConfigs)
                {
                    config(tracing);
                }

                tracing.AddSource(@this.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        @this.AddOpenTelemetryExporters();

        return @this;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder @this)
        where TBuilder : IHostApplicationBuilder
    {
        @this.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return @this;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication @this)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (@this.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            @this.MapHealthChecks("/health");

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            @this.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return @this;
    }

    private static TBuilder AddBasicServiceDefaults<TBuilder>(this TBuilder @this)
        where TBuilder : IHostApplicationBuilder
    {
        @this.AddDefaultHealthChecks();
        @this.Services.AddServiceDiscovery();
        @this.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        @this.Services.Configure<ServiceDiscoveryOptions>(options =>
        {
            options.AllowedSchemes = ["https"];
        });

        return @this;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder @this)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(@this.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            @this.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        // if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        // {
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        // }
        return @this;
    }
}
