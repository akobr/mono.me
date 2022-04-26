using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class StoringEngineBuilder : IStoringEngineBuilder
    {
        private readonly IServiceProvider services;
        private Type storerType;

        public StoringEngineBuilder(IServiceProvider services)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            storerType = typeof(IStorer);
        }

        public Task<IStoringEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default)
        {
            return Task.FromResult<IStoringEngine>(new StoringEngine((IStorer)services.GetService(storerType), properties));
        }

        public IStoringEngineBuilder SetStorer<TStorer>()
            where TStorer : IStorer
        {
            storerType = typeof(TStorer);
            return this;
        }
    }
}
