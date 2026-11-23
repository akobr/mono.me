using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// The calls of one request. The current one flows with the request's async flow, so one set of services serves every test.
internal sealed class TripwireRecorder
{
    private static readonly AsyncLocal<TripwireRecorder?> CurrentRecorder = new();

    private readonly ConcurrentQueue<TripwireCall> _calls = new();

    public static TripwireRecorder? Current
    {
        get => CurrentRecorder.Value;
        set => CurrentRecorder.Value = value;
    }

    public IReadOnlyList<TripwireCall> Calls => [.. _calls];

    // Records the call in the current request and throws, so nothing past the gate runs.
    public static Exception Trip(string service, MethodInfo member, object?[]? arguments)
    {
        var parameters = member.GetParameters();
        var byName = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        for (var i = 0; i < parameters.Length; i++)
        {
            var values = new List<string>();
            Collect(arguments is not null && i < arguments.Length ? arguments[i] : null, values, depth: 0);
            byName[parameters[i].Name ?? $"arg{i}"] = values;
        }

        var call = new TripwireCall(service, member.Name, byName);
        Current?._calls.Enqueue(call);
        return new TenantIsolationTripwireException(call);
    }

    private static void Collect(object? value, List<string> values, int depth)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                values.Add(text);
                return;
            case IEnumerable items when depth < 2:
                foreach (var item in items)
                {
                    Collect(item, values, depth + 1);
                }

                return;
        }

        var type = value.GetType();

        if (depth >= 2 || type.IsPrimitive || type.IsEnum || value is CancellationToken or DateTimeOffset or TimeSpan or Guid or decimal)
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                Collect(SafeGet(property, value), values, depth + 1);
            }
        }
    }

    private static object? SafeGet(PropertyInfo property, object target)
    {
        try
        {
            return property.GetValue(target);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }
}
