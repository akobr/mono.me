using System.Text;
using System.Text.Json;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// The pass/fail mark of a BrowserStack session. BrowserStack's executor reads the argument of an evaluate call on any
// page of the session, page.EvaluateAsync("_ => {}", "browserstack_executor: {...}"); any other Playwright server just
// evaluates the empty function. The reason is the failure's message (redacted before it is cut, so a cut never leaves
// part of a secret) on one line, cut to 255 characters; the JSON writer escapes it.
public sealed record BrowserStackSessionStatus(string Status, string? Reason)
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Expression = "_ => {}";
    public const string ExecutorPrefix = "browserstack_executor: ";
    public const int MaxReasonLength = 255;

    // browserstack_executor: {"action":"setSessionStatus","arguments":{"status":"failed","reason":"…"}}.
    public string Payload
    {
        get
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("action", "setSessionStatus");
                writer.WriteStartObject("arguments");
                writer.WriteString("status", Status);
                if (Reason is not null)
                {
                    writer.WriteString("reason", Reason);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return ExecutorPrefix + Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    // passed for a passed test; failed, with the message as the reason, for a failed, timed out, blocked or cancelled
    // one; null (the session stays unmarked) for a skipped test or no result. redact: applied to the message.
    public static BrowserStackSessionStatus? ForTest(TestResultReport? result, Func<string, string>? redact = null)
    {
        return result switch
        {
            null or { Outcome: TestOutcome.Skipped } => null,
            { Outcome: TestOutcome.Passed } => new BrowserStackSessionStatus(Passed, null),
            _ => new BrowserStackSessionStatus(Failed, ReasonOf(result, redact)),
        };
    }

    // The mark of a session several tests shared, from the last attempt of each: failed when one of them neither
    // passed nor skipped (the reason counts them and names the first), passed when one passed, otherwise null.
    public static BrowserStackSessionStatus? ForSession(IReadOnlyCollection<TestResultReport> results, Func<string, string>? redact = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        var failed = results.Where(result => result.Outcome is not (TestOutcome.Passed or TestOutcome.Skipped)).ToArray();
        if (failed.Length > 0)
        {
            var first = failed[0];
            var name = BrowserStackCapabilities.SessionName(first.Test, perTest: true);
            return new BrowserStackSessionStatus(Failed, Cut($"{failed.Length} of {results.Count} test(s) failed; {name}: {ReasonOf(first, redact)}"));
        }

        return results.Any(result => result.Outcome == TestOutcome.Passed) ? new BrowserStackSessionStatus(Passed, null) : null;
    }

    // One line of at most MaxReasonLength characters, the last an ellipsis when cut; null for blank text.
    public static string? Cut(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = string.Join(' ', text.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxReasonLength ? line : string.Concat(line.AsSpan(0, MaxReasonLength - 1), "…");
    }

    private static string ReasonOf(TestResultReport result, Func<string, string>? redact)
    {
        var message = result.Failure?.Message is { } text && redact is not null ? redact(text) : result.Failure?.Message;
        var reason = result.Outcome == TestOutcome.Failed || string.IsNullOrWhiteSpace(message)
            ? message
            : $"{result.Outcome}: {message}";
        return Cut(reason) ?? result.Outcome.ToString();
    }
}
