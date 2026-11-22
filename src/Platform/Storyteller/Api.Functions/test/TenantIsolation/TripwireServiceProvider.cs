using System.Collections.Concurrent;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// The services a handler is created with (ActivatorUtilities): the access service of the tenants, the API's JSON options,
// null loggers, the system clock, default options, a tripwire for every other interface and the platform's service classes
// over them, so a new constructor parameter needs no change here.
internal sealed class TripwireServiceProvider(IServiceProvider gate) : IServiceProvider, IServiceProviderIsService
{
    private static readonly ConcurrentDictionary<Type, object> Tripwires = new();

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        if (serviceType == typeof(IServiceProviderIsService))
        {
            return this;
        }

        if (serviceType == typeof(IAccessService))
        {
            return gate.GetRequiredService<IAccessService>();
        }

        if (serviceType == typeof(IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>))
        {
            return FunctionTestDoubles.ApiJsonOptions;
        }

        if (serviceType == typeof(TimeProvider))
        {
            return TimeProvider.System;
        }

        if (serviceType == typeof(ILogger))
        {
            return NullLogger.Instance;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            return Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(serviceType.GetGenericArguments()));
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IOptions<>))
        {
            var options = Activator.CreateInstance(serviceType.GetGenericArguments()[0]);
            return typeof(Options).GetMethod(nameof(Options.Create))!.MakeGenericMethod(serviceType.GetGenericArguments()).Invoke(null, [options]);
        }

        if (serviceType.IsInterface)
        {
            return Tripwires.GetOrAdd(serviceType, TripwireProxy.Create);
        }

        // A service class of the platform (the shared certificates of a project, for one), over tripwires itself.
        if (serviceType is { IsClass: true, IsAbstract: false } && serviceType.Namespace?.StartsWith("_42.", StringComparison.Ordinal) == true)
        {
            return ActivatorUtilities.CreateInstance(this, serviceType);
        }

        return null;
    }

    public bool IsService(Type serviceType)
    {
        return GetService(serviceType) is not null;
    }
}
