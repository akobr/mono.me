using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.GitHubActions;
using _42.Platform.Cli.Output;
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

        // The paths come from Program.Main, which reports an invalid SFORM_HOME before the host is built; the
        // properties of the host builder carry them here.
        public Startup(IHostEnvironment environment, IDictionary<object, object> properties)
        {
            Environment = environment;
            Paths = properties.TryGetValue(typeof(CliPaths), out var paths) && paths is CliPaths given
                ? given
                : CliPaths.FromEnvironment(new FileSystem(), System.Environment.GetEnvironmentVariable);
            SformEnvironment = SformEnvironment.FromEnvironment(System.Environment.GetEnvironmentVariable, Paths);
        }

        public IHostEnvironment Environment { get; }

        public CliPaths Paths { get; }

        public SformEnvironment SformEnvironment { get; }

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

            services.AddSingleton<IExtendedConsole, SformConsole>();
            services.AddHttpClient();
            ConfigureMachineSignIn(services);
            services.AddSingleton(GitHubActionsSettings.FromEnvironment(System.Environment.GetEnvironmentVariable));
            ConfigureStatePaths(services, Paths, SformEnvironment);
            services.AddSingleton<ITokenStore, ProtectedTokenStore>();
            services.AddSingleton<IAuthenticationConfigurationResolver, AuthenticationConfigurationResolver>();
            services.AddSingleton<IAuthenticationService, AuthenticationServiceSelector>();
            services.AddSingleton<ICommandContext, CommandContext>();
            services.AddSingleton<IUserSettingsStore, UserSettingsStore>();
            services.AddSingleton(EnvironmentEditor.FromEnvironment(System.Environment.GetEnvironmentVariable));
            services.AddSingleton<IEditorService, EditorService>();
            services.AddSingleton<IBrowserLauncher, BrowserLauncher>();
            services.AddSingleton<TransferPolling>();

            // McMaster's constructor injection ignores the default value of an optional parameter: every parameter of a
            // command's constructor has to be registered (CommandConstructionTests builds every command through this).
            services.AddSingleton(TimeProvider.System);

            ConfigureStorytellerSdk(services);
        }

        /// <summary>
        /// Sets default Newtonsoft.Json serializer settings (ignore null values and use indented formatting)
        /// and adds the application's JSON configuration file and the user's settings to the provided configuration builder.
        /// The settings (sform.settings.json in the .42for.net folder, or the legacy access.default.json and editor.config.json
        /// next to the assembly) come after app.config.json, so they win.
        /// </summary>
        /// <param name="builder">The configuration builder to which application JSON files are added.</param>
        public void ConfigureApplication(IConfigurationBuilder builder)
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
            };

            AddConfigurationFiles(builder, Paths);

            // Development only (a run from source, unless DOTNET_ENVIRONMENT names another environment): user secrets
            // override app.config.json.
            if (Environment.IsDevelopment())
            {
                builder.AddUserSecrets(typeof(Startup).Assembly, optional: true);
            }

            AddEnvironmentVariables(builder);
        }

        /// <summary>
        /// Adds the SFORM_ variables last, so that they win over every file: SFORM_&lt;section&gt;__&lt;key&gt;, for example
        /// SFORM_general__baseUrl for general.baseUrl. The variables of sform itself (SFORM_HOME, SFORM_TOKEN_STORE, …) have
        /// no double underscore and land as top-level keys that nothing reads.
        /// </summary>
        /// <param name="builder">The configuration builder.</param>
        internal static void AddEnvironmentVariables(IConfigurationBuilder builder)
        {
            builder.AddEnvironmentVariables(SformVariables.CONFIGURATION_PREFIX);
        }

        /// <summary>
        /// Adds the configuration files: app.config.json, which ships next to the assembly (relative to the base path of
        /// the host), then the user's settings: sform.settings.json in the user directory (the .42for.net folder, or
        /// SFORM_HOME), or the legacy access.default.json and editor.config.json of the state directory (next to the
        /// assembly, or SFORM_HOME) while the settings file has no section of their own. A missing SFORM_HOME loads as empty.
        /// </summary>
        /// <param name="builder">The configuration builder.</param>
        /// <param name="paths">The paths resolved from SFORM_HOME.</param>
        internal static void AddConfigurationFiles(IConfigurationBuilder builder, CliPaths paths)
        {
            builder.AddJsonFile(Constants.APPLICATION_CONFIG_JSON, false, false);
            builder.Add(new UserSettingsConfigurationSource(() => new UserSettingsStore(new FileSystem(), paths), WriteSettingsWarning));
        }

        /// <summary>
        /// Registers where sform keeps its files and the SFORM_* settings: the paths, the environment, and the token
        /// storage of the user's sign-in, built on first use so that the user profile is read only when needed.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="paths">The paths resolved from SFORM_HOME.</param>
        /// <param name="environment">The SFORM_* variables.</param>
        internal static void ConfigureStatePaths(IServiceCollection services, CliPaths paths, SformEnvironment environment)
        {
            services.AddSingleton(paths);
            services.AddSingleton(environment);
            services.AddSingleton(_ => new TokenStorage(paths.UserDirectory, environment.IsFileTokenStore, paths.IsHomeSet));
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
                        Paths.GetLogFilePath(options?.LogFilePath),
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

        // On standard error, so a redirected standard output stays as the command writes it.
        private static void WriteSettingsWarning(string message)
        {
            System.Console.Error.WriteLine($"! {message}");
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
