using System.IO.Abstractions;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Simulator.Configuration;
using _42.Platform.Storyteller.Simulator.Logic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog;

using Constants = _42.Platform.Storyteller.Simulator.Constants;
using IConfigurationBuilder = Microsoft.Extensions.Configuration.IConfigurationBuilder;

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

            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<IExtendedConsole, ExtendedConsole>();

            services.AddCosmosDbAnnotations(configuration);

            services.AddSingleton<CoreDbStructureBuilder>();
            services.AddSingleton<GetBaseAnnotationsReview>();
        }

        public void ConfigureApplication(IConfigurationBuilder builder)
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented,
            };

            builder.AddJsonFile(Constants.APPLICATION_CONFIG_JSON, false, false);
        }

        private void ConfigureLogging(ILoggingBuilder builder, IConfiguration configuration)
        {
            var options = configuration
                .GetSection(LoggingOptions.SECTION)
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
            services.Configure<LoggingOptions>(configuration.GetSection(LoggingOptions.SECTION));
        }
    }
}
