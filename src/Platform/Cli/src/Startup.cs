using System.IO.Abstractions;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using _42.Testing.System.IO.Abstractions;
using _42.Testing.System.IO.Abstractions.Tracers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            services.AddLogging((builder) => ConfigureLogging(builder, configuration));
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
        }

        public void ConfigureApplication(IConfigurationBuilder builder)
        {
            builder.AddJsonFile(Constants.APPLICATION_CONFIG_JSON, false, false);
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
        }
    }
}
