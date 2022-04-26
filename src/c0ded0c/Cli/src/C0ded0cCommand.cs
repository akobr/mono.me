using System;
using System.Threading.Tasks;
using c0ded0c.Core;
using c0ded0c.Core.Configuration;
using c0ded0c.Core.Genesis;
using c0ded0c.Core.Hashing;
using c0ded0c.MsBuild;
using c0ded0c.PlantUml;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;

namespace c0ded0c.Cli
{
    // [Subcommand(typeof(TCommand), typeof(TCommand)))]
    [Command("codedoc", Description = "Generates smart and nice looking documentation for your code.", ExtendedHelpText = "Your code deserves as good documentation as your RestAPI or UI components.")]
    internal class C0ded0cCommand
    {
        public async Task OnExecuteAsync()
        {
            ToolConfiguration configuration = new ToolConfiguration()
            {
                WorkingDirectory = Constants.SPARE_WORKING_DIRECTORY,
                OutputDirectory = Constants.SPARE_OUTPUT_DIRECTORY,
                RunName = Constants.SPARE_RUN_NAME,
                IsPacked = true,
                InputPaths = new[]
                {
                    @"c:\Working\c0ded0c\c0ded0c.sln",
                },
            };

            IToolBuilder builder = new ToolBuilder()
                .ConfigureServices(RegiserServices)
                .Configure(configuration)
                .UseCSharp()
                .UseCoreGenesis() // TODO: [P1] make this default
                .UseClassDiagrams()
                .UseHashMap()
                .ConfigureGenesisEngine((geb) => geb.Use<PackingGenesisMiddleware>());

            ServiceCollection servicesCollection = new ServiceCollection();
            DefaultServiceProviderFactory providerFactory = new DefaultServiceProviderFactory();
            (ITool tool, IServiceProvider services) = await builder.BuildAsync(servicesCollection, providerFactory);

            await tool.BuildAsync(new ConsoleProgressObserver());
            IIdentificationMap map = services.GetRequiredService<IIdentificationMap>();
        }

        private static void RegiserServices(IServiceCollection services)
        {
            services.AddLogging();

            services.AddSingleton<IStorer, PhysicalStorer>();

            services.AddSingleton<IPathCalculatorProvider, FullPathCalculatorProvider>();
            services.AddSingleton<IHashCalculatorProvider, HashCalculatorProvider>();
            services.AddSingleton<IIdentificationBuilder, IIdentificationMap, IdentificationManager>();

            services.AddSingleton<IArtifactContentBuilder, JsonArtifactContentBuilder>();
            services.AddSingleton<IArtifactProvider, ArtifactProvider>();
            services.AddSingleton<IArtifactBuilder, ArtifactBuilder>();
            services.AddSingleton<IArtifactManager, ArtifactManager>();
        }
    }
}
