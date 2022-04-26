using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using c0ded0c.Core.Hashing;

namespace c0ded0c.Core
{
    public class IdentificationManager : IIdentificationBuilder, IIdentificationMap
    {
        private readonly IHashCalculatorProvider calculatorProvider;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IIdentificator>> hashMap;
        private readonly ConcurrentDictionary<string, IIdentificator> fullNameMap;
        private readonly ConcurrentBag<string> collisions;
        private bool hasCollision;

        public IdentificationManager(IHashCalculatorProvider calculatorProvider)
        {
            this.calculatorProvider = calculatorProvider ?? throw new ArgumentNullException(nameof(calculatorProvider));
            hashMap = new ConcurrentDictionary<string, ConcurrentDictionary<string, IIdentificator>>(StringComparer.OrdinalIgnoreCase);
            fullNameMap = new ConcurrentDictionary<string, IIdentificator>(StringComparer.Ordinal);
            collisions = new ConcurrentBag<string>();
        }

        public int Count => fullNameMap.Count;

        public IIdentificator Build(string fullName, string name, PathCalculatorDelegate pathCalculator)
        {
            IIdentificator? identificator;

            if (fullNameMap.TryGetValue(fullName, out identificator))
            {
                return identificator;
            }

            identificator = CreateIdentificator(fullName, name, pathCalculator);
            fullNameMap.TryAdd(fullName, identificator);

            hashMap.AddOrUpdate(
                identificator.Hash,
                (key) =>
                {
                    return new ConcurrentDictionary<string, IIdentificator>(
                        new KeyValuePair<string, IIdentificator>[] { new KeyValuePair<string, IIdentificator>(fullName, identificator) },
                        StringComparer.Ordinal);
                },
                (key, value) =>
                {
                    if (!value.ContainsKey(fullName))
                    {
                        hasCollision = true;
                        collisions.Add(key);
                        value.TryAdd(fullName, identificator);
                    }

                    return value;
                });

            return identificator;
        }

        public IIdentificator? Get(string hash)
        {
            if (!hashMap.TryGetValue(hash, out var items))
            {
                return null;
            }

            return items.Values.First();
        }

        public IEnumerable<IIdentificator> GetAll(string hash)
        {
            if (!hashMap.TryGetValue(hash, out var items))
            {
                return Enumerable.Empty<IIdentificator>();
            }

            return items.Values;
        }

        public IIdentificator? GetByFullName(string fullName)
        {
            fullNameMap.TryGetValue(fullName, out IIdentificator? identificator);
            return identificator;
        }

        public IEnumerable<KeyValuePair<string, IEnumerable<IIdentificator>>> GetMap()
        {
            return hashMap.Select(kvp => KeyValuePair.Create<string, IEnumerable<IIdentificator>>(kvp.Key, kvp.Value.Values));
        }

        private IIdentificator CreateIdentificator(string fullName, string name, PathCalculatorDelegate pathCalculator)
        {
            string hash = BuildHash(fullName);
            return new Identificator(hash, fullName, name, pathCalculator(hash, fullName, name));
        }

        private string BuildHash(string fullName)
        {
            IHashCalculator calculator = calculatorProvider.GetCalculator();
            byte[] bytes = Encoding.UTF8.GetBytes(fullName);
            byte[] hash = calculator.ComputeHash(bytes);
            return calculator.ToStringRepresentation(hash);
        }
    }
}
