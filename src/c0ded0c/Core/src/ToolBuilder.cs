using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using c0ded0c.Core.Configuration;
using c0ded0c.Core.Genesis;
using c0ded0c.Core.Mining;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace c0ded0c.Core
{
    public class ToolBuilder : IToolBuilder
    {
        private readonly List<Func<IImmutableDictionary<string, string>, IImmutableDictionary<string, string>>> propertiesConfigureFunctions;
        private readonly List<Action<IServiceCollection>> servicesConfigureActions;
        private readonly List<Action<IMiningEngineBuilder>> miningConfigureActions;
        private readonly List<Action<IProjectProcessingEngineBuilder>> projectProcessingConfigureActions;
        private readonly List<Action<IAssemblyProcessingEngineBuilder>> assemblyProcessingConfigureActions;
        private readonly List<Action<IStoringEngineBuilder>> storingConfigureActions;
        private readonly List<Action<IGenesisEngineBuilder>> genesisConfigureActions;
        private readonly Mechanism mechanism;
        private IImmutableDictionary<string, string> properties;

        public ToolBuilder()
        {
            properties = ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
            propertiesConfigureFunctions = new List<Func<IImmutableDictionary<string, string>, IImmutableDictionary<string, string>>>();
            servicesConfigureActions = new List<Action<IServiceCollection>>();
            miningConfigureActions = new List<Action<IMiningEngineBuilder>>();
            projectProcessingConfigureActions = new List<Action<IProjectProcessingEngineBuilder>>();
            assemblyProcessingConfigureActions = new List<Action<IAssemblyProcessingEngineBuilder>>();
            storingConfigureActions = new List<Action<IStoringEngineBuilder>>();
            genesisConfigureActions = new List<Action<IGenesisEngineBuilder>>();
            mechanism = new Mechanism();

            // default configuration of engines
            this.UseCoreGenesis();
        }

        public async Task<(ITool Tool, IServiceProvider Services)> BuildAsync<TContainerBuilder>(
            IServiceCollection services,
            IServiceProviderFactory<TContainerBuilder> serviceProviderFactory,
            CancellationToken cancellation = default)
            where TContainerBuilder : notnull
        {
            IServiceProvider serviceProvider = await Task.Run(() => BuildServiceProvider(services, serviceProviderFactory));

            Configure(FillMandatoryProperties);
            properties = await Task.Run(BuildProperties);

            mechanism.Storing = await Task.Run(() => BuildStoringEngine(serviceProvider, cancellation));

            var miningTask = Task.Run(() => BuildMiningEngine(serviceProvider, cancellation));
            var projectProcessingTask = Task.Run(() => BuildProjectProcessingEngine(serviceProvider, cancellation));
            var assemblyProcessingTask = Task.Run(() => BuildAssemblyProcessingEngine(serviceProvider, cancellation));
            var genesisTask = Task.Run(() => BuildGenesisEngine(serviceProvider, cancellation));

            mechanism!.Mining = await miningTask;
            mechanism.ProjectPreceeding = await projectProcessingTask;
            mechanism.AssemblyPreceeding = await assemblyProcessingTask;
            mechanism.Genesis = await genesisTask;

            Tool tool = new Tool(mechanism, serviceProvider.GetService<ILogger<ITool>>());
            return (tool, serviceProvider);
        }

        public IToolBuilder Configure(
            Func<IImmutableDictionary<string, string>, IImmutableDictionary<string, string>> configure)
        {
            propertiesConfigureFunctions.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
            return this;
        }

        public IToolBuilder ConfigureServices(Action<IServiceCollection> configure)
        {
            servicesConfigureActions.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
            return this;
        }

        public IToolBuilder ConfigureMiningEngine(Action<IMiningEngineBuilder> configure)
        {
            miningConfigureActions.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
            return this;
        }

        public IToolBuilder ConfigureProjectProcessingEngine(Action<IProjectProcessingEngineBuilder> configure)
        {
            projectProcessingConfigureActions.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
            return this;
        }

        public IToolBuilder ConfigureAssemblyProcessingEngine(Action<IAssemblyProcessingEngineBuilder> configure)
        {
            assemblyProcessingConfigureActions.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
            return this;
        }

        public IToolBuilder ConfigureStoringEngine(Action<IStoringEngineBuilder> configure)
        {
            storingConfigureActions.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
            return this;
        }

        public IToolBuilder ConfigureGenesisEngine(Action<IGenesisEngineBuilder> configure)
        {
            genesisConfigureActions.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
            return this;
        }

        private IImmutableDictionary<string, string> BuildProperties()
        {
            var resultProperties = properties;

            foreach (var configure in propertiesConfigureFunctions)
            {
                resultProperties = configure(resultProperties) ?? throw new InvalidOperationException("Configure properties function returns null.");
            }


            return resultProperties;
        }

        private Task<IMiningEngine> BuildMiningEngine(IServiceProvider serviceProvider, CancellationToken cancellation)
        {
            var builder = new MiningEngineBuilder(serviceProvider);

            foreach (var configure in miningConfigureActions)
            {
                cancellation.ThrowIfCancellationRequested();
                configure(builder);
            }

            return builder.BuildAsync(properties, cancellation);
        }

        private Task<IProjectProcessingEngine> BuildProjectProcessingEngine(IServiceProvider serviceProvider, CancellationToken cancellation)
        {
            var builder = new ProjectProcessingEngineBuilder(serviceProvider);

            foreach (var configure in projectProcessingConfigureActions)
            {
                cancellation.ThrowIfCancellationRequested();
                configure(builder);
            }

            return builder.BuildAsync(properties, cancellation);
        }

        private Task<IAssemblyProcessingEngine> BuildAssemblyProcessingEngine(IServiceProvider serviceProvider, CancellationToken cancellation)
        {
            var builder = new AssemblyProcessingEngineBuilder(serviceProvider);

            foreach (var configure in assemblyProcessingConfigureActions)
            {
                cancellation.ThrowIfCancellationRequested();
                configure(builder);
            }

            return builder.BuildAsync(properties, cancellation);
        }

        private Task<IStoringEngine> BuildStoringEngine(IServiceProvider serviceProvider, CancellationToken cancellation)
        {
            var builder = new StoringEngineBuilder(serviceProvider);

            foreach (var configure in storingConfigureActions)
            {
                cancellation.ThrowIfCancellationRequested();
                configure(builder);
            }

            return builder.BuildAsync(properties, cancellation);
        }

        private Task<IGenesisEngine> BuildGenesisEngine(IServiceProvider serviceProvider, CancellationToken cancellation)
        {
            var builder = new GenesisEngineBuilder(serviceProvider);

            foreach (var configure in genesisConfigureActions)
            {
                cancellation.ThrowIfCancellationRequested();
                configure(builder);
            }

            return builder.BuildAsync(properties, cancellation);
        }

        private IServiceProvider BuildServiceProvider<TContainerBuilder>(
            IServiceCollection services,
            IServiceProviderFactory<TContainerBuilder> serviceProviderFactory)
        {
            ConfigureCoreServices(services);

            foreach (var configure in servicesConfigureActions)
            {
                configure(services);
            }

            TContainerBuilder containerBuilder = serviceProviderFactory.CreateBuilder(services);
            return serviceProviderFactory.CreateServiceProvider(containerBuilder);
        }

        private static IImmutableDictionary<string, string> FillMandatoryProperties(IImmutableDictionary<string, string> properties)
        {
            if (!properties.ContainsKey(PropertyNames.WorkingDirectory))
            {
                properties = properties.SetItem(PropertyNames.WorkingDirectory, Constants.SPARE_WORKING_DIRECTORY);
            }

            if (!properties.ContainsKey(PropertyNames.OutputDirectory))
            {
                properties = properties.SetItem(PropertyNames.OutputDirectory, Constants.SPARE_OUTPUT_DIRECTORY);
            }

            if (!properties.ContainsKey(PropertyNames.RunName))
            {
                properties = properties.SetItem(PropertyNames.RunName, Constants.SPARE_RUN_NAME);
            }

            return properties;
        }

        private void ConfigureCoreServices(IServiceCollection services)
        {
            services.AddSingleton<IMechanism>(mechanism);

            services.AddSingleton<IMiningEngine>((services) => services.GetService<IMechanism>().Mining);
            services.AddSingleton<IProjectProcessingEngine>((services) => services.GetService<IMechanism>().ProjectPreceeding);
            services.AddSingleton<IAssemblyProcessingEngine>((services) => services.GetService<IMechanism>().AssemblyPreceeding);
            services.AddSingleton<IStoringEngine>((services) => services.GetService<IMechanism>().Storing);
            services.AddSingleton<IGenesisEngine>((services) => services.GetService<IMechanism>().Genesis);
        }
    }
}
