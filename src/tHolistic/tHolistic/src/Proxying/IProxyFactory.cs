namespace _42.tHolistic;

public interface IProxyFactory
{
    TInterface CreateInterfaceProxy<TInterface>()
        where TInterface : class;

    object CreateInterfaceProxy(Type interfaceType);

    TInterface CreateInterfaceProxy<TInterface, TImplementation>()
        where TInterface : class
        where TImplementation : TInterface, new();

    object CreateInterfaceProxy(Type interfaceType, object target);

    object CreateStepsProxy(Type outputType, object[]? constructorArguments = null);

    object CreateStepsProxy(Type outputType, object target, object[]? constructorArguments = null);

    object CreateStepsProxy(object target);
}
