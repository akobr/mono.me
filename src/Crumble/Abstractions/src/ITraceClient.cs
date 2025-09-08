namespace _42.Crumble;

public interface ITraceClient
{
    // IActivity BeginActivity(string activityName);

    // IMeasurement BeginMeasurement(string measurementName);

    void TraceAction(string actionName);
}
