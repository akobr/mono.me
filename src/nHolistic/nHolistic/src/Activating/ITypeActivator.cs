namespace _42.nHolistic;

public interface ITypeActivator
{
    TType Activate<TType>(TestCase testCase);

    object Activate(TestCase testCase, Type type);
}
