using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Sdk.Hosting;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleExecutor;

// The lines of ExecutorCommand on a plain writer: the toolkit's renderer wraps lines at the width of the console, and the
// text of a format must stay as it is.
public sealed class ExecutionRenderer
{
    private readonly TextWriter _output;

    public ExecutionRenderer(TextWriter output)
    {
        _output = output;
    }

    public void WriteWho(StorytellerExecutionContext self, DateTimeOffset now)
    {
        _output.WriteLine($"Who am I: {self.Key}");
        _output.WriteLine($"  Type: {self.Annotation.Type}");

        if (!string.IsNullOrEmpty(self.Annotation.Title))
        {
            _output.WriteLine($"  Title: {self.Annotation.Title}");
        }

        _output.WriteLine($"  Status: {self.GetSubscriptionStatus(now)}");

        if (self.Ancestors.Count > 0)
        {
            var ancestors = self.Ancestors.OrderBy(pair => pair.Key).Select(pair => pair.Value.AnnotationKey);
            _output.WriteLine($"  Ancestors: {string.Join(", ", ancestors)}");
        }

        if (self.Unreadable.Count > 0)
        {
            _output.WriteLine($"  Unreadable: {string.Join(", ", self.Unreadable)}");
        }
    }

    public void WriteExecution(string headline, StorytellerExecution execution, StorytellerAnnotation? usage, DateTimeOffset now)
    {
        _output.WriteLine($"{headline} {execution.AnnotationKey}: {execution.GetStatus(now)}");

        if (usage is not null)
        {
            var values = usage.Values.Select(pair => $", {pair.Key}={pair.Value}");
            _output.WriteLine($"  Usage {usage.AnnotationKey}: {usage.GetStatus(now)}{string.Concat(values)}");
        }

        if (execution.UnitNames.Count > 0)
        {
            _output.WriteLine($"  Units: {string.Join(", ", execution.UnitNames)}");
        }
    }

    public void WriteRemoved(StorytellerExecutionChange change)
    {
        _output.WriteLine($"Removed {change.Execution.AnnotationKey}: {change.Removal?.AnnotationKey} is {change.Removal?.Reason}");
    }

    public void WriteNoExecutions(string responsibility, string context)
    {
        _output.WriteLine($"No active execution of {responsibility} in {context}.");
    }

    // The exact text of the format, under a line that names it.
    public void WriteDocument(string key, long version, FormattedConfiguration body)
    {
        _output.WriteLine($"--- {key}, version {version}, {body.Format.GetName()}");
        _output.Write(body.Text);
    }
}
