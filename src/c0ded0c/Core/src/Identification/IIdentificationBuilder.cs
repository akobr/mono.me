using System;

namespace c0ded0c.Core
{
    public interface IIdentificationBuilder
    {
        IIdentificator Build(string fullName, string name, PathCalculatorDelegate pathCalculator);
    }
}
