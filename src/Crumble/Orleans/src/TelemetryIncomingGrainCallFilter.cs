using System.Diagnostics;
using OpenTelemetry.Context.Propagation;

namespace _42.Crumble;

public class TelemetryIncomingGrainCallFilter : IIncomingGrainCallFilter
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // Build a “carrier” from RequestContext
        var carrier = new Dictionary<string, string>();
        foreach (var key in RequestContext.Keys)
        {
            if (RequestContext.Get(key) is string v)
            {
                carrier[key] = v;
            }
        }

        var parent = Propagator.Extract(default, carrier, (c, k) =>
            c.TryGetValue(k, out var val) ? new[] { val } : Array.Empty<string>());

        /*using var activity =
            Telemetry.ActivitySource.StartActivity(
                $"{context.Grain.GetType().Name}.{context.InterfaceMethod.Name}",
                ActivityKind.Server,
                parent.ActivityContext);

        // (optional) add useful tags
        activity?.SetTag("orleans.grain", context.Grain.GetType().FullName);
        activity?.SetTag("orleans.interface", context.InterfaceMethod.DeclaringType?.FullName);
        activity?.SetTag("orleans.method", context.InterfaceMethod.Name);*/

        await context.Invoke();
    }
}
