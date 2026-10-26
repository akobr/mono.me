using System;
using System.IO.Abstractions;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog;
using IConfigurationBuilder = Microsoft.Extensions.Configuration.IConfigurationBuilder;

namespace _42.Platform.Cli
{
    public class Startup : IStartup
    {
        private const string ClientCertificateHeaderName = "X-ARR-ClientCert";

        public Startup(IHostEnvironment environment)
        {
            Environment = environment;
        }

        public IHostEnvironment Environment { get; }

        /// <summary>
        /// Configures dependency injection, logging, application options, the platform file system implementation, core singleton services, and the Storyteller SDK for the CLI host.
        /// </summary>
        /// <param name="services">The service collection to configure with application services and options.</param>
        /// <param name="configuration">The application configuration used to bind option classes and to configure logging and SDK settings.</param>
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddLogging(builder => ConfigureLogging(builder, configuration));
            ConfigureOptions(configuration, services);

#if !DEBUG || TESTING
            services.AddSingleton<IFileSystem, FileSystem>();
#else
            services.AddSingleton<IFileSystem>(_ => new DiagnosticFileSystem(
                new ReadonlyFileSystem(new FileSystem()),
                new ConsoleFileSystemTracer()));
#endif

            services.AddSingleton<IExtendedConsole, ExtendedConsole>();
            services.AddHttpClient();
            ConfigureMachineSignIn(services);
            services.AddSingleton<ITokenStore, ProtectedTokenStore>();
            services.AddSingleton<IAuthenticationConfigurationResolver, AuthenticationConfigurationResolver>();
            services.AddSingleton<IAuthenticationService, AuthenticationServiceSelector>();
            services.AddSingleton<ICommandContext, CommandContext>();
            services.AddSingleton<IEditorService, EditorService>();
            services.AddSingleton<TransferPolling>();

            ConfigureStorytellerSdk(services);
        }

        /// <summary>
        /// Sets default Newtonsoft.Json serializer settings (ignore null values and use indented formatting)
        /// and adds the application's JSON configuration files to the provided configuration builder.
        /// </summary>
        /// <param name="builder">The configuration builder to which application JSON files are added.</param>
        public void ConfigureApplication(IConfigurationBuilder builder)
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
            };

            builder.AddJsonFile(Constants.APPLICATION_CONFIG_JSON, false, false);
            builder.AddJsonFile(Constants.ACCESS_DEFAULT_JSON, true, false);
            builder.AddJsonFile(Constants.EDITOR_CONFIG_JSON, true, false);
        }

        /// <summary>
        /// Registers the non-interactive sign-in of a machine from the STORYTELLER_* environment variables.
        /// With machine credentials, STORYTELLER_ENDPOINT (or the deprecated STORYTELLER_API_URL) replaces general.baseUrl;
        /// a user session is never sent to it. The SDK credential makes its token requests on its own client.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        internal static void ConfigureMachineSignIn(IServiceCollection services)
        {
            services.AddSingleton(MachineSignInSettings.FromEnvironment(System.Environment.GetEnvironmentVariable));
            services.AddSingleton<MachineAuthenticationService>();
            services.AddSingleton<IMachineSignIn>(provider => provider.GetRequiredService<MachineAuthenticationService>());

            services.AddOptions<GeneralOptions>().PostConfigure<MachineSignInSettings>((options, machine) =>
            {
                if (machine.IsConfigured && machine.Endpoint is not null)
                {
                    options.BaseUrl = machine.Endpoint;
                }
            });
        }

        /// <summary>
        /// Registers the clients of the Storyteller SDK with the token of the IAuthenticationService the commands use.
        /// After a 401, a machine with a token it can renew (client credentials or federated) sends the request once more
        /// with a new token.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        internal static void ConfigureStorytellerSdk(IServiceCollection services)
        {
            // Registered before AddStorytellerSdk, which only adds a configuration when none exists,
            // so the token comes from the same IAuthenticationService the commands use.
            services.AddSingleton<ISdkConfiguration>(provider =>
            {
                var baseUrl = provider.GetRequiredService<IOptions<GeneralOptions>>().Value.BaseUrl;
                var machine = provider.GetRequiredService<IMachineSignIn>();

                return new SdkConfiguration
                {
                    AccessTokenFactory = () =>
                    {
                        try
                        {
                            var authentication = provider.GetRequiredService<IAuthenticationService>();
                            return authentication.GetAccessTokenAsync().GetAwaiter().GetResult() ?? string.Empty;
                        }
                        catch (Exception exception) when (exception is not AuthenticationException)
                        {
                            // A sign-in that cannot work is reported; anything else ends as a 401 from the API.
                            return string.Empty;
                        }
                    },
                    BaseUrl = baseUrl,

                    // Null when the certificate does not load; the token factory reports why before any request.
                    ClientCertificate = machine.TryGetCertificate(),
                    ClientCertificateHeaderName = IsLocalHttp(baseUrl) ? ClientCertificateHeaderName : null,
                };
            });

            // Only on the typed clients: the token requests of the SDK credential and the AuthKit client get no resend.
            services.AddStorytellerSdk(null, builder => builder.AddHttpMessageHandler(
                provider => new MachineTokenRenewalHandler(provider.GetRequiredService<IMachineSignIn>())));
        }

        /// <summary>
        /// Configures the application's logging providers and sinks (Serilog, file sink, Sentry) and enables debug logging in the development environment.
        /// </summary>
        /// <param name="builder">The logging builder to configure.</param>
        /// <param name="configuration">Application configuration; the logging section is read from ConfigurationSections.LOGGING for file sink settings.</param>
        private void ConfigureLogging(ILoggingBuilder builder, IConfiguration configuration)
        {
            var options = configuration
                .GetSection(ConfigurationSections.LOGGING)
                .Get<LoggingOptions>();

            if (Environment.IsDevelopment())
            {
                builder.AddDebug();
            }

            builder.AddSerilog(
                new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    .WriteTo.Sentry(
                        minimumEventLevel: Serilog.Events.LogEventLevel.Warning)
                    .WriteTo.File(
                        options?.GetTemplateLogFullPath() ?? "logs/cli.log",
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true)
                    .CreateLogger());
        }

        /// <summary>
        /// Registers and binds specific configuration sections to their corresponding options types in the dependency injection container.
        /// </summary>
        /// <param name="configuration">The application configuration root from which the LOGGING, ACCESS, AUTHENTICATION, GENERAL, EDITOR, and TELEMETRY sections are read.</param>
        /// <param name="services">The service collection where the configured options are registered.</param>
        private static void ConfigureOptions(IConfiguration configuration, IServiceCollection services)
        {
            services.Configure<LoggingOptions>(configuration.GetSection(ConfigurationSections.LOGGING));
            services.Configure<AccessDefaultOptions>(configuration.GetSection(ConfigurationSections.ACCESS));
            services.Configure<AuthenticationOptions>(configuration.GetSection(ConfigurationSections.AUTHENTICATION));
            services.Configure<GeneralOptions>(configuration.GetSection(ConfigurationSections.GENERAL));
            services.Configure<EditorOptions>(configuration.GetSection(ConfigurationSections.EDITOR));
            services.Configure<TelemetryOptions>(configuration.GetSection(ConfigurationSections.TELEMETRY));
        }

        // The local Functions host has no TLS handshake to carry a client certificate, so the
        // certificate goes in the header it accepts in development (MachineAuth:IsCertificateHeaderFromClientAllowed).
        private static bool IsLocalHttp(string? baseUrl)
        {
            return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                && StorytellerUrlRule.IsLocalHttp(uri);
        }
    }
}
