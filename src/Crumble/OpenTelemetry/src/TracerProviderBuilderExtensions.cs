using OpenTelemetry.Trace;

namespace _42.Crumble.OpenTelemetry;

public static class TracerProviderBuilderExtensions
{
    public static TracerProviderBuilder AddMyLibTracing(this TracerProviderBuilder b)
        => b.AddSource(Telemetry.ACTIVITY_SOURCE_NAME);
}
