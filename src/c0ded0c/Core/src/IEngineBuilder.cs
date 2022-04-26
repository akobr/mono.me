using System;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IEngineBuilder<TInput, TOutput, TMiddlewareInterface>
        where TMiddlewareInterface : class
    {
        IEngineBuilder<TInput, TOutput, TMiddlewareInterface> Use(
            Func<TInput, Func<TInput, Task<TOutput>>, Task<TOutput>> middleware);

        IEngineBuilder<TInput, TOutput, TMiddlewareInterface> Use<TMiddleware>()
            where TMiddleware : class, TMiddlewareInterface;
    }

    public interface IEngineBuilder<TImmutableSubject, TMiddlewareInterface>
        : IEngineBuilder<TImmutableSubject, TImmutableSubject, TMiddlewareInterface>
        where TMiddlewareInterface : class
    {
        // no members
    }
}
