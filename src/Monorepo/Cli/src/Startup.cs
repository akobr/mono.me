using System.Collections.Generic;
using System.IO;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Operations;
using _42.Monorepo.Cli.Operations.Strategies;
using _42.Monorepo.Cli.Output;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Semver;

namespace _42.Monorepo.Cli
{
    public class Startup : IStartup
    {
        public Startup(IHostEnvironment environment)
        {
            Environment = environment;
        }

        public IHostEnvironment Environment { get; }

        public static void ConfigureOpStrategies(IOpStrategiesRegister register)
        {
            register.RegisterFuncStrategy<VersionFilePathOpStrategy>(OperationNames.VERSION_FILE_PATH);
            register.RegisterFuncStrategy<PackagesFilePathOpStrategy>(OperationNames.PACKAGES_FILE_PATH);
            register.RegisterFuncStrategy<DefinedVersionOpStrategy>(OperationNames.DEFINED_VERSION);
            register.RegisterFuncStrategy<ExactVersionsOpStrategy>(OperationNames.EXACT_VERSIONS);
            register.RegisterFuncStrategy<AllReleasesOpStrategy>(OperationNames.ALL_RELEASES);
            register.RegisterFuncStrategy<LastReleaseOpStrategy>(OperationNames.LAST_RELEASE);
            register.RegisterFuncStrategy<ExternalDependenciesOpStrategy>(OperationNames.EXTERNAL_DEPENDENCIES);
            register.RegisterFuncStrategy<InternalDependenciesOpStrategy>(OperationNames.INTERNAL_DEPENDENCIES);
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<OpStrategiesFactory>();
            services.AddSingleton<IOpStrategiesRegister>(sp => sp.GetRequiredService<OpStrategiesFactory>());
            services.AddSingleton<IOpStrategiesFactory>(sp =>
            {
                var factory = sp.GetRequiredService<OpStrategiesFactory>();
                ConfigureOpStrategies(factory);
                return factory;
            });

            services.AddSingleton<ICommandContext, CommandContext>();
            services.AddSingleton<IFileContentCache, FileContentCache>();
            services.AddSingleton<IGenericOpsCache, GenericOpsCache>();
            services.AddSingleton<IOpsExecutor, OpsExecutor>();
            services.AddSingleton<IItemsFactory, ItemsFactory>();
            services.AddSingleton<IItemOptionsProvider, ItemOptionsProvider>();
            services.AddSingleton<IExtendedConsole, ExtendedConsole>();

            services.AddSingleton<IGitRepositoryFactory, GitRepositoryFactory>();
            services.AddSingleton<ITagsProvider, TagsProvider>();
        }

        public void ConfigureOptions(IConfiguration configuration, IServiceCollection services)
        {
            services.Configure<ReleaseOptions>(configuration.GetSection(ConfigurationSections.Release));
        }

        public void ConfigureApplication(IConfigurationBuilder builder)
        {
            string repoRootPath = MonorepoDirectoryFunctions.GetMonorepoRootDirectory();
            string repoConfigFilePath = Path.Combine(repoRootPath, Constants.MONOREPO_CONFIG_JSON);

            if (!File.Exists(repoConfigFilePath))
            {
                return;
            }

            builder.AddJsonFile(repoConfigFilePath, true, true);
        }
    }
}
