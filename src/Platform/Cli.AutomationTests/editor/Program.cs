using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _42.Platform.Cli.AutomationTests.Editor;

// The fake editor of the CLI automation tests (spec §4.3, mode E of docs/Platform/Cli.AutomationTests/test-cases.md).
// sform starts the custom command of its editor (the editor section of sform.settings.json) with the quoted path of the
// file to edit appended (EditorService.ParseCustomCommand) and waits for it to exit: 0 makes sform read the file back, any other code makes it
// say "Editor exited with code N." and end with 1. What this editor does comes from the variables the test sets for the
// run of sform (inherited by the editor; FakeEditor and EditorChange of the tests write them):
//   CLI_EDITOR_MODE      the step of each invocation, comma-separated; the n-th invocation of a run does the n-th step and
//                        the last step repeats:
//                          replace       the file becomes the content of the step's file
//                          merge         the step's file is an RFC 7396 JSON merge patch applied to the file
//                          none          the file is left as it is (sform: "No changes detected.", 42)
//                          invalid       the file becomes text that is not JSON (sform: "Invalid JSON: …")
//                          exit[:<code>] the file is left as it is and the editor exits with the code (1 by default)
//   CLI_EDITOR_FILE      the file of replace and merge
//   CLI_EDITOR_FILE_<n>  the file of the n-th step, over CLI_EDITOR_FILE
//   CLI_EDITOR_LOG       a file that gets one JSON line per invocation (EditorLog of the tests reads it); its lines also
//                        count the invocations, so a step list needs it
//   CLI_EDITOR_RUN       a name of the run of sform, written into each line: the invocations are counted per run, so one
//                        log serves several runs of a test
// The last argument is the file to edit; arguments before it (a custom command "<editor> --flag") are only logged. It
// writes nothing to its standard output, which it shares with sform; a problem goes to the standard error and the log.
// Exit codes besides those of exit:<code>: 64 no file argument, 65 CLI_EDITOR_MODE missing or unknown, 66 the file to edit
// or the step's file does not exist, 67 merge of a text that is not JSON.
public static class Program
{
    public const string ModeVariable = "CLI_EDITOR_MODE";
    public const string FileVariable = "CLI_EDITOR_FILE";
    public const string LogVariable = "CLI_EDITOR_LOG";
    public const string RunVariable = "CLI_EDITOR_RUN";

    private const int UsageError = 64;
    private const int ModeError = 65;
    private const int MissingFile = 66;
    private const int NotJson = 67;

    private const string InvalidJson = "{ \"written by\": \"the fake editor of the CLI automation tests\", \"this is\": not JSON";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions Written = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions LogLine = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Main(string[] args)
    {
        return Execute(args, new FileSystem(), Environment.GetEnvironmentVariable, Console.Error);
    }

    // The whole editor, with its environment and file system given (the tests' self tests start the built apphost instead).
    public static int Execute(string[] args, IFileSystem fileSystem, Func<string, string?> variable, System.IO.TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(error);

        var logPath = variable(LogVariable) is { Length: > 0 } log ? fileSystem.Path.GetFullPath(log) : null;
        var run = variable(RunVariable) is { Length: > 0 } named ? named : null;
        var entry = new LogEntry
        {
            Invocation = CountInvocations(fileSystem, logPath, run) + 1,
            Run = run,
            Time = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Arguments = args,
            File = args.Length > 0 ? fileSystem.Path.GetFullPath(args[^1]) : null,
        };

        entry.ExitCode = Edit(entry, fileSystem, variable);
        if (entry.Error is not null)
        {
            error.WriteLine($"fake editor: {entry.Error}");
        }

        if (logPath is not null)
        {
            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(logPath)!);
            fileSystem.File.AppendAllText(logPath, JsonSerializer.Serialize(entry, LogLine) + "\n", Utf8);
        }

        return entry.ExitCode;
    }

    private static int Edit(LogEntry entry, IFileSystem fileSystem, Func<string, string?> variable)
    {
        if (entry.File is null)
        {
            return Fail(entry, UsageError, "no file to edit: sform appends its path to the custom command.");
        }

        if (!fileSystem.File.Exists(entry.File))
        {
            return Fail(entry, MissingFile, $"the file to edit does not exist: {entry.File}");
        }

        entry.Original = fileSystem.File.ReadAllText(entry.File);

        var steps = (variable(ModeVariable) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (steps.Length == 0)
        {
            return Fail(entry, ModeError, $"{ModeVariable} is not set: replace, merge, none, invalid or exit[:<code>], comma-separated per invocation.");
        }

        var stepNumber = Math.Min(entry.Invocation, steps.Length);
        var step = steps[stepNumber - 1];
        entry.Step = step;
        var name = step.Split(':', 2)[0].ToLowerInvariant();

        switch (name)
        {
            case "none":
                return 0;

            case "invalid":
                Write(entry, fileSystem, InvalidJson);
                return 0;

            case "exit":
                var code = step.Contains(':', StringComparison.Ordinal) ? step.Split(':', 2)[1] : "1";
                return int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode)
                    ? exitCode
                    : Fail(entry, ModeError, $"the step '{step}' has no exit code as a number.");

            case "replace":
            case "merge":
                var stepFileVariable = $"{FileVariable}_{stepNumber.ToString(CultureInfo.InvariantCulture)}";
                var changeFile = variable(stepFileVariable) is { Length: > 0 } own ? own : variable(FileVariable);
                if (string.IsNullOrEmpty(changeFile) || !fileSystem.File.Exists(changeFile))
                {
                    return Fail(entry, MissingFile, $"the step '{step}' needs {stepFileVariable} (or {FileVariable}) naming an existing file; it is '{changeFile}'.");
                }

                entry.ChangeFile = fileSystem.Path.GetFullPath(changeFile);
                var change = fileSystem.File.ReadAllText(changeFile);
                if (name == "replace")
                {
                    Write(entry, fileSystem, change);
                    return 0;
                }

                JsonNode? target;
                JsonNode? patch;
                try
                {
                    target = JsonNode.Parse(entry.Original);
                    patch = JsonNode.Parse(change);
                }
                catch (JsonException exception)
                {
                    return Fail(entry, NotJson, $"merge needs JSON in the file to edit and in the patch: {exception.Message}");
                }

                var merged = MergePatch(target, patch);
                Write(entry, fileSystem, merged is null ? "null" : merged.ToJsonString(Written));
                return 0;

            default:
                return Fail(entry, ModeError, $"unknown step '{step}' in {ModeVariable}; use replace, merge, none, invalid or exit[:<code>].");
        }
    }

    // RFC 7396: an object merges member by member (null removes a member), anything else replaces the target.
    private static JsonNode? MergePatch(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObject)
        {
            return patch?.DeepClone();
        }

        var result = target as JsonObject ?? new JsonObject();
        foreach (var (name, value) in patchObject.ToList())
        {
            if (value is null)
            {
                result.Remove(name);
                continue;
            }

            if (value is JsonObject && result[name] is JsonObject current)
            {
                MergePatch(current, value);
                continue;
            }

            result.Remove(name);
            result[name] = MergePatch(null, value);
        }

        return result;
    }

    private static void Write(LogEntry entry, IFileSystem fileSystem, string text)
    {
        fileSystem.File.WriteAllText(entry.File!, text, Utf8);
        entry.Written = text;
    }

    private static int Fail(LogEntry entry, int code, string message)
    {
        entry.Error = message;
        return code;
    }

    // The earlier invocations of the run in the log (every line without a run name).
    private static int CountInvocations(IFileSystem fileSystem, string? logPath, string? run)
    {
        if (logPath is null || !fileSystem.File.Exists(logPath))
        {
            return 0;
        }

        return fileSystem.File.ReadAllLines(logPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Count(line => run is null || RunOf(line) == run);
    }

    private static string? RunOf(string line)
    {
        try
        {
            return JsonNode.Parse(line)?["run"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // One line of CLI_EDITOR_LOG.
    private sealed class LogEntry
    {
        public int Invocation { get; set; }

        public string? Run { get; set; }

        public string Time { get; set; } = string.Empty;

        public string? File { get; set; }

        public IReadOnlyList<string> Arguments { get; set; } = [];

        public string? Step { get; set; }

        public string? ChangeFile { get; set; }

        public int ExitCode { get; set; }

        public string? Original { get; set; }

        public string? Written { get; set; }

        public string? Error { get; set; }
    }
}
