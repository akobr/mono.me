using c0ded0c.Core;
using Microsoft.CodeAnalysis;

namespace c0ded0c.MsBuild
{
    public interface IPathCalculatorProvider
    {
        public PathCalculatorDelegate GetAssemblyPathCalculator(IAssemblySymbol assembly);

        public PathCalculatorDelegate GetProjectPathCalculator();

        public PathCalculatorDelegate GetDocumentPathCalculator(IIdentificator assemblyKey, string relativePath);

        public PathCalculatorDelegate GetNamespacePathCalculator(IIdentificator assemblyKey);

        public PathCalculatorDelegate GetTypePathCalculator(IIdentificator documentKey, IIdentificator assemblyKey);

        public PathCalculatorDelegate GetMemberPathCalculator(IIdentificator typeKey, IIdentificator documentKey, IIdentificator assemblyKey);
    }
}
