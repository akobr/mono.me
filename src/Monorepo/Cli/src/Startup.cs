using System.IO;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Operations;
using _42.Monorepo.Cli.Operations.Strategies;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Scripting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace _42.Monorepo.Cli
{
    public class Startup : IStartup
    {
        public Startup(IHostEnvironment environment)
        {
            Environment = environment;
        }

        public IHostEnvironment Environment { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(ConfigureLogging);
            services.AddOpStrategies(ConfigureOpStrategies);

            services.AddSingleton<ICommandContext, CommandContext>();
            services.AddSingleton<IFileContentCache, FileContentCache>();
            services.AddSingleton<IItemsFactory, ItemsFactory>();
            services.AddSingleton<IExtendedConsole, ExtendedConsole>();
            services.AddSingleton<IGitRepositoryFactory, GitRepositoryFactory>();
            services.AddSingleton<ITagsProvider, TagsProvider>();

            services.AddSingleton<IScriptingService, ScriptingService>();
        }

        public void ConfigureOptions(IConfiguration configuration, IServiceCollection services)
        {
            services.AddSingleton<IItemOptionsProvider, ItemOptionsProvider>();
            services.AddSingleton<ITypeOptionsProvider, TypeOptionsProvider>();

            services.Configure<MonoRepoOptions>(configuration.GetSection(ConfigurationSections.REPO));
            services.Configure<ReleaseOptions>(configuration.GetSection(ConfigurationSections.RELEASE));
        }

        public void ConfigureApplication(IConfigurationBuilder builder)
        {
            var repoRootPath = MonorepoDirectoryFunctions.GetMonorepoRootDirectory();
            var repoConfigFilePath = Path.Combine(repoRootPath, Constants.MONOREPO_CONFIG_JSON);

            if (!File.Exists(repoConfigFilePath))
            {
                return;
            }

            // TODO: load logging settings
            builder.AddJsonFile(repoConfigFilePath, true, true);
        }

        private void ConfigureLogging(ILoggingBuilder builder)
        {
            // TODO: add sentry and serilog
#if DEBUG
            builder.AddDebug();
#endif
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
