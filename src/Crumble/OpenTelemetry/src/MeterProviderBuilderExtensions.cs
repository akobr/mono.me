using OpenTelemetry.Metrics;

namespace _42.Crumble.OpenTelemetry;

public static class MeterProviderBuilderExtensions
{
    public static MeterProviderBuilder AddMyLibMetrics(this MeterProviderBuilder b)
        => b.AddMeter(Telemetry.METER_NAME);
}
