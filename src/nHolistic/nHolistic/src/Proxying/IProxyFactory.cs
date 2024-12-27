namespace _42.nHolistic;

public interface IProxyFactory
{
    TInterface CreateInterfaceProxy<TInterface>()
        where TInterface : class;

    object CreateInterfaceProxy(Type interfaceType);

    TInterface CreateInterfaceProxy<TInterface, TImplementation>()
        where TInterface : class
        where TImplementation : TInterface, new();

    object CreateInterfaceProxy(Type interfaceType, object target);

    object CreateStepsProxy(Type classType, object[] constructorArguments);

    object CreateStepsProxy(Type classType, object target, object[] constructorArguments);
}
