using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using _42.Platform.Cli.ErrorReporting;
using _42.Platform.Cli.Telemetry;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.ErrorReporting;

// Seeded texts and values built of the characters the rules care about (masks, quotes, escapes, separators, shapes):
// the scrubbing ends, throws nothing, and keeps no value it was given whole.
public class ReportScrubberFuzzTests
{
    private const int Cases = 1500;

    private static readonly string[] Fragments =
    [
        "*", "**", "***", "[Filtered]", "[", "]", "'", "\"", "%22", "\\n", "\\", "/", ":", "://", "=", "@", ".", "-", " ",
        "acme", "billing", "Zq7", "x9", "é", "7c9e6679-7425-40de-944b-e07fc1f90ae7", "ada@acme.example", "https://api.acme.example/v1/x",
        "C:\\Users\\ada\\out.zip", "/home/ada/out.zip", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln", "Password=", "{\"value\":",
    ];

    // One budget for every case, generous for a loaded agent: a scrubbing that does not end fails it.
    [Fact]
    public void TheScrubbing_Ends_ThrowsNothing_AndKeepsNoValueItWasGivenWhole()
    {
        Should.CompleteIn(Run, TimeSpan.FromSeconds(60));
    }

    private static void Run()
    {
        var random = new Random(42_75);
        var leaks = new List<string>();

        for (var index = 0; index < Cases; index++)
        {
            var value = Build(random, random.Next(1, 8));
            var text = $"{Build(random, random.Next(0, 6))}{(random.Next(3) == 0 ? value.ToUpperInvariant() : value)}{Build(random, random.Next(0, 6))}";
            var values = CommandLineValues.WithParts(value).ToList();

            var secrets = ReportScrubber.ScrubSecrets(text, values);
            var sentry = ReportScrubber.ScrubForSentry(text, values);

            // A value of mask characters can be matched by the masks themselves; the check is for the others.
            if (value.Trim().Length >= 6 && value.IndexOfAny(['*', '[', ']', '\r', '\n']) < 0)
            {
                if (secrets.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    leaks.Add($"secrets: [{text}] [{value}] -> [{secrets}]");
                }

                if (text.IndexOfAny(['\r', '\n']) < 0 && sentry.Length < ReportScrubber.MaxSentryMessageLength
                    && sentry.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    leaks.Add($"sentry: [{text}] [{value}] -> [{sentry}]");
                }
            }
        }

        leaks.ShouldBeEmpty();
    }

    private static string Build(Random random, int count)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < count; index++)
        {
            builder.Append(Fragments[random.Next(Fragments.Length)]);
        }

        return builder.ToString();
    }
}
