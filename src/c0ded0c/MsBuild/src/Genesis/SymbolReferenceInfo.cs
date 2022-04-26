using System.Collections.Generic;
using c0ded0c.Core;

namespace c0ded0c.MsBuild.Genesis
{
    public class SymbolReferenceInfo
    {
        public SymbolReferenceInfo(IIdentificator symbolKey, IReadOnlyCollection<PositionInfo> positions)
        {
            Symbol = symbolKey;
            Positions = positions;
        }

        public IIdentificator Symbol { get; }

        public IReadOnlyCollection<PositionInfo> Positions { get; }
    }
}
