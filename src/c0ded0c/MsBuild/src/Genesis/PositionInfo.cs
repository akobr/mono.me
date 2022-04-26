using c0ded0c.Core;
using Microsoft.CodeAnalysis.Text;

namespace c0ded0c.MsBuild.Genesis
{
    public class PositionInfo
    {
        public PositionInfo(IIdentificator documentKey, TextSpan source)
        {
            Document = documentKey;
            Start = source.Start;
            Length = source.Length;
        }

        public IIdentificator Document { get; }

        public int Start { get; set; }

        public int Length { get; set; }
    }
}
