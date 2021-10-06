using System;
using System.Collections.Generic;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Monorepo.Cli.Operations
{
    public class OpStrategiesFactory : IOpStrategiesFactory, IOpStrategiesRegister
    {
        private readonly IServiceProvider services;
        private readonly Dictionary<int, Type> strategies;

        public OpStrategiesFactory(IServiceProvider services)
        {
            this.services = services;
            strategies = new Dictionary<int, Type>();
        }

        public IOpStrategy<T> BuildStrategy<T>(IItem item, string operationKey)
        {
            var key = operationKey.GetHashCode();

            if (!strategies.TryGetValue(key, out var type))
            {
                throw new InvalidOperationException($"The operation '{operationKey}' is not registered.");
            }

            return (IOpStrategy<T>)ActivatorUtilities.CreateInstance(services, type);
        }

        public IOpStrategy BuildStrategy(IItem item, string operationKey)
        {
            var key = operationKey.GetHashCode();
            var type = strategies[key];
            return (IOpStrategy)ActivatorUtilities.CreateInstance(services, type);
        }

        public void RegisterActionStrategy<T>(string operationKey)
            where T : class, IOpStrategy
        {
            var key = operationKey.GetHashCode();
            strategies[key] = typeof(T);
        }

        public void RegisterFuncStrategy<T>(string operationKey)
            where T : class, IOpStrategyGeneric
        {
            var key = operationKey.GetHashCode();
            strategies[key] = typeof(T);
        }

        private static int CalculateKey(ItemType itemType, string operationKey)
        {
            return HashCode.Combine(itemType, operationKey);
        }
    }
}
