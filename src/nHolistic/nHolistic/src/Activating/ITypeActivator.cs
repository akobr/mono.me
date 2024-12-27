using System.Reflection;

namespace _42.nHolistic;

public interface ITypeActivator
{
    object Activate(Type type, TestCase? testCase);

    object[] ResolveParameters(ParameterInfo[] parameters, TestCase? testCase);
}
