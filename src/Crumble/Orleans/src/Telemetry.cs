using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace _42.Crumble;

public static class Telemetry
{
    public const string ACTIVITY_SOURCE_NAME = "42.Crumble";
    public const string METER_NAME = "42.Crumble";

    public static readonly ActivitySource ActivitySource = new(ACTIVITY_SOURCE_NAME, typeof(Telemetry).Assembly.GetName().Version!.ToString());
    public static readonly Meter Meter = new(METER_NAME, typeof(Telemetry).Assembly.GetName().Version!.ToString());

    public static readonly Counter<long> Crumbs =
        Meter.CreateCounter<long>("crumble.crumbs", unit: "1", description: "Number of processed crumbs");

    public static readonly Histogram<double> CrumbDurationMs =
        Meter.CreateHistogram<double>("crumble.crumb.duration", unit: "ms", description: "End-to-end and phase durations of crumb");
}
