using System.IO.Abstractions;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Sdk;
using _42.Platform.Sdk.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog;

namespace _42.Platform.Cli
{
    public class Startup : IStartup
    {
        public Startup(IHostEnvironment environment)
        {
            Environment = environment;
        }

        public IHostEnvironment Environment { get; }

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
            services.AddSingleton<IAuthenticationService, AuthenticationService>();
            services.AddSingleton<ICommandContext, CommandContext>();

            services.AddPlatformSdk();

            // TODO: [P2] Make this better
            var authOptions = configuration.GetSection(ConfigurationSections.AUTHENTICATION).Get<AuthenticationOptions>();
            var generalOptions = configuration.GetSection(ConfigurationSections.GENERAL).Get<GeneralOptions>();
            var authService = new AuthenticationService(new FileSystem(), new OptionsWrapper<AuthenticationOptions>(authOptions!));
            GlobalConfiguration.Instance = new DynamicConfiguration
            {
                AccessTokenFactory = () => authService.GetAuthenticationAsync().Result!.AccessToken,
                BasePath = generalOptions!.BaseUrl,
                UserAgent = $"{generalOptions.UserAgent}/{ThisAssembly.AssemblyInformationalVersion} ({System.Environment.OSVersion.Platform:G}; {System.Environment.OSVersion.VersionString})",
            };
        }

        public void ConfigureApplication(IConfigurationBuilder builder)
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
            };

            builder.AddJsonFile(Constants.APPLICATION_CONFIG_JSON, false, false);
            builder.AddJsonFile(Constants.ACCESS_DEFAULT_JSON, true, false);
        }

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
                        initializeSdk: false,
                        minimumEventLevel: Serilog.Events.LogEventLevel.Warning)
                    .WriteTo.File(
                        options?.GetTemplateLogFullPath() ?? "logs/cli.log",
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true)
                    .CreateLogger());
        }

        private static void ConfigureOptions(IConfiguration configuration, IServiceCollection services)
        {
            services.Configure<LoggingOptions>(configuration.GetSection(ConfigurationSections.LOGGING));
            services.Configure<AccessDefaultOptions>(configuration.GetSection(ConfigurationSections.ACCESS));
            services.Configure<AuthenticationOptions>(configuration.GetSection(ConfigurationSections.AUTHENTICATION));
        }
    }
}
