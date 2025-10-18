using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace _42.Crumble;

public class TelemetryOutgoingGrainCallFilter : IOutgoingGrainCallFilter
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        var carrier = new Dictionary<string, string>();
        var activity = System.Diagnostics.Activity.Current;

        Propagator.Inject(
            new PropagationContext(activity?.Context ?? default, Baggage.Current),
            carrier,
            (c, k, v) => c[k] = v);

        // Copy keys into Orleans RequestContext
        foreach (var kv in carrier)
            RequestContext.Set(kv.Key, kv.Value);

        try
        {
            await context.Invoke();
        }
        finally
        {
            foreach (var key in carrier.Keys)
                RequestContext.Remove(key);
        }
    }
}
