using Castle.DynamicProxy;
using Microsoft.Extensions.DependencyInjection;

namespace _42.tHolistic;

public class ProxyFactory(
    ITestRunContextProvider provider,
    IServiceProvider services)
    : IProxyFactory
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

    public object CreateStepsProxy(Type outputType, object[]? constructorArguments = null)
    {
        var runContext = provider.GetContext();
        var interceptor = new ClassStepsInterceptor(runContext);
        return _proxyGenerator.CreateClassProxy(outputType, constructorArguments, interceptor);
    }

    public object CreateStepsProxy(Type outputType, object target, object[]? constructorArguments = null)
    {
        var runContext = provider.GetContext();
        var interceptor = new ClassStepsInterceptor(runContext);
        return _proxyGenerator.CreateClassProxyWithTarget(outputType, target, constructorArguments, interceptor);
    }

    public object CreateStepsProxy(object target)
    {
        var type = target.GetType();
        var constructor = type.GetConstructors().FirstOrDefault();

        if (constructor is null)
        {
            throw new InvalidOperationException($"Type {type} does not have a public constructor. A proxy object can't be created.");
        }

        // TODO: [P2] not sure if it is robust enough or if there is a smarter way to do it
        var parameters = constructor.GetParameters();
        var args = new object[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            args[i] = services.GetRequiredService(parameters[i].ParameterType);
        }

        var runContext = provider.GetContext();
        var interceptor = new ClassStepsInterceptor(runContext);
        return _proxyGenerator.CreateClassProxyWithTarget(target.GetType(), target, args, interceptor);
    }
}
