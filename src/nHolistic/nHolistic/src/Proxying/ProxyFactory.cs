using Castle.DynamicProxy;

namespace _42.nHolistic;

public class ProxyFactory(ITestRunContextProvider provider) : IProxyFactory
{
    private readonly ProxyGenerator _proxyGenerator = new();

    public TInterface CreateInterfaceProxy<TInterface>()
        where TInterface : class
    {
        var interceptor = new PropertyStorageInterceptor();
        return _proxyGenerator.CreateInterfaceProxyWithoutTarget<TInterface>(interceptor);
    }

    public object CreateInterfaceProxy(Type interfaceType)
    {
        var interceptor = new PropertyStorageInterceptor();
        return _proxyGenerator.CreateInterfaceProxyWithoutTarget(interfaceType, interceptor);
    }

    public TInterface CreateInterfaceProxy<TInterface, TImplementation>()
        where TInterface : class
        where TImplementation : TInterface, new()
    {
        var interceptor = new PropertyStorageInterceptor();
        return _proxyGenerator.CreateInterfaceProxyWithTarget<TInterface>(new TImplementation(), interceptor);
    }

    public object CreateInterfaceProxy(Type interfaceType, object target)
    {
        var interceptor = new PropertyStorageInterceptor();
        return _proxyGenerator.CreateInterfaceProxyWithTarget(interfaceType, target, interceptor);
    }

    public object CreateStepsProxy(Type classType, object[] constructorArguments)
    {
        var runContext = provider.GetContext();
        var interceptor = new ClassStepsInterceptor(runContext);
        return _proxyGenerator.CreateClassProxy(classType, constructorArguments, interceptor);
    }

    public object CreateStepsProxy(Type classType, object target, object[] constructorArguments)
    {
        var runContext = provider.GetContext();
        var interceptor = new ClassStepsInterceptor(runContext);
        return _proxyGenerator.CreateClassProxyWithTarget(classType, target, constructorArguments, interceptor);
    }
}
