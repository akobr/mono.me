using System.Reflection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// A service of a handler that must not be reached for another tenant: every member records the call and throws.
internal class TripwireProxy : DispatchProxy
{
    private string _service = string.Empty;

    public static object Create(Type serviceType)
    {
        var proxy = DispatchProxy.Create(serviceType, typeof(TripwireProxy));
        ((TripwireProxy)proxy)._service = serviceType.Name;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        throw TripwireRecorder.Trip(_service, targetMethod!, args);
    }
}
