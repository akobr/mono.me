using System.Globalization;
using System.Text;

namespace _42.Testing.UI.Automation.Reporting.Testiny;

// The markdown comment of one result on a linked case. Testiny has no duration field on a result, so the duration lives
// here, with the attempt, the browser (engine, version, channel, provider, os, device), the seed, the remote session, the
// run, the build, the artifacts and what became of them, and the failure: the exception's type and message, and its
// stack trace (the ToString form, with the inner exceptions) in a fenced block.
internal static class TestinyResultComment
{
    public const int MaxStackTraceLength = 20_000;

    private const int MaxMessageLength = 1_000;

    public static string Build(TestResultReport report, TestRunDescriptor run, string status, IReadOnlyList<TestinyArtifactNote> artifacts, long maxAttachmentBytes)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(artifacts);

        var test = report.Test;
        var builder = new StringBuilder();
        builder.Append("**").Append(TestinyMarkdown.Text(status)).Append("** ").Append(TestinyMarkdown.Code(ShortName(test))).Append("\n\n");

        Line(builder, "Test", TestinyMarkdown.Code($"{test.ClassName}.{test.DisplayName}"));
        Line(builder, "Outcome", report.Outcome.ToString());
        Line(builder, "Duration", TestinyMarkdown.Duration(report.Duration));
        Line(builder, "Started", TestinyMarkdown.Time(report.StartedAt));
        Line(builder, "Attempt", report.Attempt.ToString(CultureInfo.InvariantCulture));
        Line(builder, "Browser", Browser(report, run));
        Line(builder, "Seed", TestinyMarkdown.Text(Property(report, run, TestRunProperties.Seed)));
        Line(builder, "Remote session", TestinyMarkdown.Text(Property(report, run, TestRunProperties.RemoteSession)));
        Line(builder, "Run", $"{TestinyMarkdown.Text(run.Name)} ({TestinyMarkdown.Code(run.RunId)})");
        Line(builder, "Build", Property(report, run, TestRunProperties.BuildUrl) is { } build ? $"<{build}>" : null);

        if (report.Failure is { SourceFile: { } file, SourceLine: { } line })
        {
            Line(builder, "Source", TestinyMarkdown.Code(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(file)}:{line}")));
        }

        if (artifacts.Count > 0)
        {
            Line(builder, "Artifacts", string.Join(", ", artifacts.Select(note => Describe(note, maxAttachmentBytes))));
        }

        if (report.Failure is { } failure)
        {
            builder.Append('\n');
            if (failure.ExceptionType is null)
            {
                builder.Append("**Reason:** ").Append(TestinyMarkdown.Text(TestinyMarkdown.Truncate(failure.Message, MaxMessageLength))).Append('\n');
            }
            else
            {
                builder.Append("**").Append(TestinyMarkdown.Text(failure.ExceptionType)).Append(":** ")
                    .Append(TestinyMarkdown.Text(TestinyMarkdown.Truncate(FirstLine(failure.Message), MaxMessageLength))).Append("\n\n")
                    .Append(TestinyMarkdown.Fence(TestinyMarkdown.Truncate(failure.StackTrace ?? failure.Message, MaxStackTraceLength))).Append('\n');
            }
        }

        return TestinyMarkdown.Document(builder.ToString().TrimEnd('\n'));
    }

    // SignInTests.SignIn_ShowsOrganizations: the class without its namespace and the display name.
    public static string ShortName(TestIdentity test)
    {
        ArgumentNullException.ThrowIfNull(test);

        var lastDot = test.ClassName.LastIndexOfAny(['.', '+']);
        return $"{(lastDot >= 0 ? test.ClassName[(lastDot + 1)..] : test.ClassName)}.{test.DisplayName}";
    }

    private static void Line(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append("- **").Append(label).Append(":** ").Append(value).Append('\n');
        }
    }

    // A value of the result, else of the run.
    private static string? Property(TestResultReport report, TestRunDescriptor run, string key)
    {
        return report.Properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value
            : run.Properties.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value) ? value
            : null;
    }

    // chromium 141.0.7390.37, channel chrome, provider BrowserStack, os Windows 11, device Pixel 7.
    private static string? Browser(TestResultReport report, TestRunDescriptor run)
    {
        var parts = new List<string>();
        var engine = string.Join(' ', new[] { Property(report, run, TestRunProperties.Engine), Property(report, run, TestRunProperties.BrowserVersion) }.Where(part => part is not null));
        if (engine.Length > 0)
        {
            parts.Add(engine);
        }

        Add(parts, "channel", Property(report, run, TestRunProperties.Channel));
        Add(parts, "provider", Property(report, run, TestRunProperties.Provider));
        Add(parts, "os", Property(report, run, TestRunProperties.Os));
        Add(parts, "device", Property(report, run, TestRunProperties.Device));
        return parts.Count == 0 ? null : TestinyMarkdown.Text(string.Join(", ", parts));

        static void Add(List<string> parts, string label, string? value)
        {
            if (value is not null)
            {
                parts.Add($"{label} {value}");
            }
        }
    }

    private static string Describe(TestinyArtifactNote note, long maxAttachmentBytes)
    {
        var state = note.State switch
        {
            TestinyArtifactState.Attached => $"attached, {TestinyMarkdown.Size(note.SizeBytes)}",
            TestinyArtifactState.NotAttached => "not attached",
            TestinyArtifactState.TooLarge => $"not attached: {TestinyMarkdown.Size(note.SizeBytes)} is over the limit of {TestinyMarkdown.Size(maxAttachmentBytes)}",
            TestinyArtifactState.Missing => "not attached: the file is gone",
            _ => "the upload failed",
        };

        return $"{TestinyMarkdown.Code(note.Artifact.Name)} ({state})";
    }

    private static string FirstLine(string text)
    {
        var end = text.AsSpan().IndexOfAny('\r', '\n');
        return end >= 0 ? text[..end] : text;
    }
}
