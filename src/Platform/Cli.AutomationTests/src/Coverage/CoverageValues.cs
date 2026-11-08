using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// coverage-values.json: the options whose values come from a fixed set (spec §4.1), each value an item to cover as
// "--option=value". The CLI validates most of them in code (a string option), so the file is the source of the set and
// says where it lives ("source"); when the code parses the value into an enum, "enum" names it, and the coverage test
// checks the set against the enum's members: "values" are the members but those in "except" (the CLI itself refuses
// them, exit 2 before any call) and in "refused" (the CLI sends them and the platform refuses them), plus those in "extra"
// (values it takes besides the enum). A member the CLI accepts and acts on is a value, however odd, so a case has to show
// what it does. An option whose own type is an enum must be listed, with exactly the enum's names (but "except" and
// "refused"). Unknown members fail the load, so a misspelled field is not silently dropped.
public sealed class CoverageValues
{
    public const string FileName = "coverage-values.json";

    // "$comment": what the file is, for its readers.
    [JsonPropertyName("$comment")]
    public string? Comment { get; init; }

    public IReadOnlyList<Entry> Options { get; init; } = [];

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static CoverageValues Load(string? path = null, IFileSystem? fileSystem = null)
    {
        return LoadJson<CoverageValues>(FileName, path, fileSystem);
    }

    // The entry of a command's option (by the command's canonical path and any form of the option); CheckValues reports an
    // entry whose path is not canonical, which this would never find.
    public Entry? For(CliCommand command, CliOption option)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(option);

        return Options.FirstOrDefault(entry =>
            entry.Command is not null
            && entry.Option is not null
            && string.Equals(Canonical(entry.Command), command.IsRoot ? string.Empty : command.Path, StringComparison.Ordinal)
            && option.Forms.Contains(entry.Option, StringComparer.Ordinal));
    }

    // A command path as For compares it: its words without a leading "sform", single-spaced.
    public static string Canonical(string path) => string.Join(' ', CommandTree.Words(path));

    // One of the two json files next to the test assembly, with the file's name in any error.
    internal static T LoadJson<T>(string fileName, string? path, IFileSystem? fileSystem)
        where T : class
    {
        fileSystem ??= new FileSystem();
        path ??= fileSystem.Path.Combine(AppContext.BaseDirectory, fileName);
        if (!fileSystem.File.Exists(path))
        {
            throw new InvalidOperationException($"{fileName} is not next to the test assembly ({path}); it is copied there by the build.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(fileSystem.File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"{path} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{path} does not read as {fileName} (a misspelled or unknown field, or invalid JSON): {exception.Message}", exception);
        }
    }

    public sealed record Entry
    {
        [JsonPropertyName("$comment")]
        public string? Comment { get; init; }

        // The canonical command path without sform ("storyteller kv set"; "sform" for the root).
        public string? Command { get; init; }

        // A form of the option, the long one by convention ("--protection").
        public string? Option { get; init; }

        // The values as the catalogue writes them ("public", "Reader"); compared without case.
        public IReadOnlyList<string> Values { get; init; } = [];

        // Where the set lives in the CLI's code, and where the CLI or the platform refuses the members left out.
        public string? Source { get; init; }

        // The enum (full name, in 42.Platform.Cli.dll or another 42.*.dll next to it) the code parses the value into.
        public string? Enum { get; init; }

        // Members of the enum the CLI itself refuses (exit 2 before any call): kv protect --protection public.
        public IReadOnlyList<string> Except { get; init; } = [];

        // Members of the enum the CLI passes on and the platform refuses (the API answers with an error): --role None.
        // Not values to cover, as nothing of the CLI acts on them; "source" says where the platform refuses them.
        public IReadOnlyList<string> Refused { get; init; } = [];

        // Values the option takes besides the enum's members.
        public IReadOnlyList<string> Extra { get; init; } = [];

        public override string ToString()
        {
            return $"{Command} {Option}";
        }
    }
}
