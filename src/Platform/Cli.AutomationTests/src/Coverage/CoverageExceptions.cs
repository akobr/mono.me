using System.IO.Abstractions;
using System.Text.Json.Serialization;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// coverage-exceptions.json (spec §4.2): the items of the command tree that only tests a target may lack the feature for
// ([RequiresFeature]: BYOK, HSM, …) or that never run ([Skip]) cover, each with the reason. Their tests exist and skip
// where the feature is missing; the coverage test checks that every item covered only by such tests is listed here and
// that nothing listed is covered by a test that runs everywhere (a stale entry). Unknown members fail the load.
public sealed class CoverageExceptions
{
    public const string FileName = "coverage-exceptions.json";

    // "$comment": what the file is, for its readers.
    [JsonPropertyName("$comment")]
    public string? Comment { get; init; }

    public IReadOnlyList<Entry> Exceptions { get; init; } = [];

    public static CoverageExceptions Load(string? path = null, IFileSystem? fileSystem = null)
    {
        return CoverageValues.LoadJson<CoverageExceptions>(FileName, path, fileSystem);
    }

    public sealed record Entry
    {
        [JsonPropertyName("$comment")]
        public string? Comment { get; init; }

        // The command path (canonical or with aliases, without sform; "sform" for the root).
        public string? Command { get; init; }

        // What of the command: an option form ("--byok-key"), a fixed value ("--protection=byok"), an argument ("arg0"), an
        // alias ("trash"); null or missing for the command itself.
        public string? Item { get; init; }

        // The feature of Target:Features its tests require (CliFeatures), when the reason is a feature.
        public string? Feature { get; init; }

        // Why no test that runs against every target covers it, e.g. the case and the feature.
        public string? Reason { get; init; }

        public override string ToString()
        {
            return Item is null ? $"{Command}" : $"{Command} {Item}";
        }
    }
}
