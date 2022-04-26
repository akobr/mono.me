using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public abstract class BaseEngine<TInput, TOutput> : IEngine
    {
        private readonly IImmutableList<Func<TInput, Func<TInput, Task<TOutput>>, Task<TOutput>>> middlewares;
        private Func<TInput, Task<TOutput>> dumpMiddleware;

        protected BaseEngine(
            IImmutableList<Func<TInput, Func<TInput, Task<TOutput>>, Task<TOutput>>> middlewares,
            Func<TInput, Task<TOutput>> dumpMiddleware,
            IImmutableDictionary<string, string> properties)
        {
            this.middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
            this.dumpMiddleware = dumpMiddleware ?? throw new ArgumentNullException(nameof(dumpMiddleware));
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        }

        protected IImmutableDictionary<string, string> Properties { get; private set; }

        public Task ConfigureAsync(CancellationToken cancellation = default)
        {
            return OnConfigure(cancellation);
        }

        protected virtual Task OnConfigure(CancellationToken cancellation)
        {
            return Task.CompletedTask;
        }

        protected Task<TOutput> Run(TInput input)
        {
            return PrepareMiddleware()(input);
        }

        private Func<TInput, Task<TOutput>> PrepareMiddleware(int index = 0)
        {
            return index < middlewares.Count
                ? ((input) => middlewares[index](input, PrepareMiddleware(index + 1)))
                : dumpMiddleware;
        }
    }
}
