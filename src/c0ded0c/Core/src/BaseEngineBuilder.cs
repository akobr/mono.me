using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace c0ded0c.Core
{
    public abstract class BaseEngineBuilder<TInput, TOutput, TMiddlewareInterface>
        : IEngineBuilder<TInput, TOutput, TMiddlewareInterface>
        where TMiddlewareInterface : class
    {
        private readonly List<Func<TInput, Func<TInput, Task<TOutput>>, Task<TOutput>>> middlewares;
        private readonly List<Lazy<TMiddlewareInterface>> middlewareInstances;

        public BaseEngineBuilder(IServiceProvider services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            middlewares = new List<Func<TInput, Func<TInput, Task<TOutput>>, Task<TOutput>>>();
            middlewareInstances = new List<Lazy<TMiddlewareInterface>>();
        }

        protected IServiceProvider Services { get; }

        public IEngineBuilder<TInput, TOutput, TMiddlewareInterface> Use(
            Func<TInput, Func<TInput, Task<TOutput>>, Task<TOutput>> middleware)
        {
            middlewares.Add(middleware ?? throw new ArgumentNullException(nameof(middleware)));
            return this;
        }

        public IEngineBuilder<TInput, TOutput, TMiddlewareInterface> Use<TMiddleware>()
            where TMiddleware : class, TMiddlewareInterface
        {
            Type middlewareType = typeof(TMiddleware);
            ConstructorInfo constuctor = GetMiddlewareTargetConstructor(middlewareType);
            Lazy<TMiddlewareInterface> instance = new Lazy<TMiddlewareInterface>(() => BuildMiddlewareInstance(constuctor), LazyThreadSafetyMode.None);
            middlewareInstances.Add(instance);
            middlewares.Add(BuildMiddlewareFunc(instance));
            return this;
        }

        protected abstract Func<TInput, Func<TInput, Task<TOutput>>, Task<TOutput>> BuildMiddlewareFunc(Lazy<TMiddlewareInterface> instance);

        protected IImmutableList<Func<TInput, Func<TInput, Task<TOutput>>, Task<TOutput>>> GetMiddlewares()
        {
            return ImmutableList.Create(middlewares.ToArray());
        }

        protected void BuildMiddlewareInstancies(IImmutableDictionary<string, string> properties)
        {
            foreach (var middleware in middlewareInstances)
            {
                if (middleware.Value is IInitialisableWithProperties initialisable)
                {
                    initialisable.Initialise(properties);
                }
            }
        }

        private TMiddlewareInterface BuildMiddlewareInstance(ConstructorInfo constuctor)
        {
            var parameters = constuctor.GetParameters();
            object[] args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                args[i] = Services.GetRequiredService(parameters[i].ParameterType);
            }

            return (TMiddlewareInterface)constuctor.Invoke(args);
        }

        private static ConstructorInfo GetMiddlewareTargetConstructor(Type middlewareType)
        {
            ConstructorInfo? targetConstructor = null;
            var constructors = middlewareType.GetConstructors();

            if (constructors.Length < 1)
            {
                throw new ArgumentException("The middleware has no public constructor.");
            }
            else if (constructors.Length == 1)
            {
                targetConstructor = constructors[0];
            }
            else if (constructors.Length > 1)
            {
                foreach (var constructor in constructors
                    .Where(constructor => constructor.GetCustomAttributes(typeof(ActivatorUtilitiesConstructorAttribute), true).Length > 0))
                {
                    if (targetConstructor != null)
                    {
                        throw new ArgumentException($"The middleware contains multiple constructors decorated with {nameof(ActivatorUtilitiesConstructorAttribute)}.");
                    }

                    targetConstructor = constructor;
                }

                if (targetConstructor == null)
                {
                    throw new ArgumentException($"The middleware contains multiple constructors, then one and only one must be decorated with {nameof(ActivatorUtilitiesConstructorAttribute)}.");
                }
            }

            return targetConstructor ?? throw new ArgumentException("The middleware has no valid constructor.");
        }
    }
}
