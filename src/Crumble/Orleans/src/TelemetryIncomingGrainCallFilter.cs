using System.Diagnostics;
using OpenTelemetry.Context.Propagation;
using Orleans;
using Orleans.Runtime;

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

        await context.Invoke();
    }
}
