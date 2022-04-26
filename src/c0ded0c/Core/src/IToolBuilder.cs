using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using c0ded0c.Core.Genesis;
using c0ded0c.Core.Mining;
using Microsoft.Extensions.DependencyInjection;

namespace c0ded0c.Core
{
    public interface IToolBuilder
    {
        IToolBuilder Configure(
            Func<IImmutableDictionary<string, string>, IImmutableDictionary<string, string>> configure);

        IToolBuilder ConfigureServices(Action<IServiceCollection> configure);

        IToolBuilder ConfigureMiningEngine(Action<IMiningEngineBuilder> configure);

        IToolBuilder ConfigureProjectProcessingEngine(Action<IProjectProcessingEngineBuilder> configure);

        IToolBuilder ConfigureAssemblyProcessingEngine(Action<IAssemblyProcessingEngineBuilder> configure);

        IToolBuilder ConfigureStoringEngine(Action<IStoringEngineBuilder> configure);

        IToolBuilder ConfigureGenesisEngine(Action<IGenesisEngineBuilder> configure);

        // TODO: Genesis
        Task<(ITool Tool, IServiceProvider Services)> BuildAsync<TContainerBuilder>(
            IServiceCollection services,
            IServiceProviderFactory<TContainerBuilder> serviceProviderFactory,
            CancellationToken cancellation = default)
            where TContainerBuilder : notnull;
    }
}