using System.IO;
using System.IO.Abstractions;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Features;
using _42.Monorepo.Cli.Git;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Operations;
using _42.Monorepo.Cli.Operations.Strategies;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Scripting;
using _42.Monorepo.Texo.Core.Markdown;
using _42.Testing.System.IO.Abstractions;
using _42.Testing.System.IO.Abstractions.Tracers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace _42.Monorepo.Cli
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
            services.AddOpStrategies(ConfigureOpStrategies);

#if !DEBUG || TESTING
            services.AddSingleton<IFileSystem, FileSystem>();
#else
            services.AddSingleton<IFileSystem>(_ => new DiagnosticFileSystem(
                new ReadonlyFileSystem(new FileSystem()),
                new ConsoleFileSystemTracer()));
#endif

            services.AddSingleton<ICommandContext, CommandContext>();
            services.AddSingleton<IFileContentCache, FileContentCache>();
            services.AddSingleton<IItemsFactory, ItemsFactory>();
            services.AddSingleton<IExtendedConsole, ExtendedConsole>();

            services.AddSingleton<IGitRepositoryService, GitRepositoryService>();
            services.AddSingleton<IGitTagsService, GitTagsService>();
            services.AddSingleton<IScriptingService, ScriptingService>();
            services.AddSingleton<IMarkdownService, MarkdownService>();
            services.AddSingleton<IFeatureProvider, FeatureProvider>();
            services.AddSingleton<IGitHistoryService, GitHistoryService>();
        }

        public void ConfigureApplication(IConfigurationBuilder builder)
        {
            builder.AddJsonFile(Constants.APPLICATION_CONFIG_JSON, false, false);

            var repoRootPath = MonorepoDirectoryFunctions.GetMonorepoRootDirectory();
            var repoConfigFilePath = Path.Combine(repoRootPath, Constants.MONOREPO_CONFIG_JSON);

            if (File.Exists(repoConfigFilePath))
            {
                builder.AddJsonFile(repoConfigFilePath, true, true);
            }
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
            services.AddSingleton<IItemOptionsProvider, ItemOptionsProvider>();
            services.AddSingleton<ITypeOptionsProvider, TypeOptionsProvider>();
            services.AddSingleton<IItemFullOptionsProvider, ItemFullOptionsProvider>();

            services.Configure<LoggingOptions>(configuration.GetSection(ConfigurationSections.LOGGING));
            services.Configure<MonoRepoOptions>(configuration.GetSection(ConfigurationSections.REPO));
            services.Configure<ReleaseOptions>(configuration.GetSection(ConfigurationSections.RELEASE));
        }

        private static void ConfigureOpStrategies(IOpStrategiesRegister register)
        {
            register.RegisterFuncStrategy<VersionFilePathOpStrategy>(OperationNames.VERSION_FILE_PATH);
            register.RegisterFuncStrategy<PackagesFilePathOpStrategy>(OperationNames.PACKAGES_FILE_PATH);
            register.RegisterFuncStrategy<DefinedVersionOpStrategy>(OperationNames.DEFINED_VERSION);
            register.RegisterFuncStrategy<ExactVersionsOpStrategy>(OperationNames.EXACT_VERSIONS);
            register.RegisterFuncStrategy<AllReleasesOpStrategy>(OperationNames.ALL_RELEASES);
            register.RegisterFuncStrategy<LastReleaseOpStrategy>(OperationNames.LAST_RELEASE);
            register.RegisterFuncStrategy<ExternalDependenciesOpStrategy>(OperationNames.EXTERNAL_DEPENDENCIES);
            register.RegisterFuncStrategy<InternalDependenciesOpStrategy>(OperationNames.INTERNAL_DEPENDENCIES);
            register.RegisterFuncStrategy<PackageNameProjectOpStrategy>(OperationNames.PACKAGE_NAME);
            register.RegisterFuncStrategy<IsPackableProjectOpStrategy>(OperationNames.IS_PACKABLE);
        }
    }
}
