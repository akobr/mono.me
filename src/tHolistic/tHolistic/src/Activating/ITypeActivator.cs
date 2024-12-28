using System.Reflection;

namespace _42.tHolistic;

public interface ITypeActivator
{
    object Activate(Type type, TestCase? testCase);

    object[] ResolveParameters(ParameterInfo[] parameters, TestCase? testCase);
}
