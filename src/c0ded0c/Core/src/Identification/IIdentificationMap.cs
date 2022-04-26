using System.Collections.Generic;

namespace c0ded0c.Core
{
    public interface IIdentificationMap
    {
        int Count { get; }

        IIdentificator? Get(string hash);

        IEnumerable<IIdentificator> GetAll(string hash);

        IIdentificator? GetByFullName(string fullName);

        IEnumerable<KeyValuePair<string, IEnumerable<IIdentificator>>> GetMap();
    }
}
