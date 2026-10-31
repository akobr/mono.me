using System.IO.Abstractions;
using System.Text;
using System.Text.Json;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

// <ArtifactsPath>/<RunId>/results.jsonl: the run and its planned tests on the first line, then one line per reported
// result, appended as they come, so a crashed or offline run can be sent again later (Reporting:ReplayFile).
public sealed class ResultsFile
{
    public const string FileName = "results.jsonl";

    private const string RunKind = "run";
    private const string ResultKind = "result";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IFileSystem fileSystem;
    private readonly object gate = new();

    public ResultsFile(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        this.fileSystem = fileSystem;
        Path = path;
    }

    public string Path { get; }

    // Starts the file (replacing one of the same path) with the run line.
    public void WriteRun(TestRunDescriptor run, IReadOnlyList<TestIdentity> plannedTests)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plannedTests);

        var line = Serialize(new ResultsFileLine(RunKind, Run: run, PlannedTests: plannedTests));
        lock (gate)
        {
            var directory = fileSystem.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                fileSystem.Directory.CreateDirectory(directory);
            }

            fileSystem.File.WriteAllText(Path, line, Utf8);
        }
    }

    public void Append(TestResultReport result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var line = Serialize(new ResultsFileLine(ResultKind, Result: result));
        lock (gate)
        {
            fileSystem.File.AppendAllText(Path, line, Utf8);
        }
    }

    // Throws FileNotFoundException for a missing file; lines that do not parse are skipped with a warning.
    public static ResultsFileContent Read(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!fileSystem.File.Exists(path))
        {
            throw new FileNotFoundException($"The results file {path} does not exist.", path);
        }

        TestRunDescriptor? run = null;
        var planned = new List<TestIdentity>();
        var results = new List<TestResultReport>();
        var warnings = new List<string>();
        var number = 0;

        foreach (var text in fileSystem.File.ReadAllLines(path, Utf8))
        {
            number++;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            ResultsFileLine? line;
            try
            {
                line = JsonSerializer.Deserialize(text, ResultsFileJsonContext.Default.ResultsFileLine);
            }
            catch (JsonException exception)
            {
                warnings.Add($"{path}, line {number}, was skipped: {exception.Message}");
                continue;
            }

            switch (line)
            {
                case { Kind: RunKind, Run: not null }:
                    run = line.Run;
                    planned.AddRange(line.PlannedTests ?? []);
                    break;

                case { Kind: ResultKind, Result: not null }:
                    results.Add(line.Result);
                    break;

                default:
                    warnings.Add($"{path}, line {number}, was skipped: neither a run nor a result.");
                    break;
            }
        }

        return new ResultsFileContent(run, planned, results, warnings);
    }

    private static string Serialize(ResultsFileLine line)
    {
        return JsonSerializer.Serialize(line, ResultsFileJsonContext.Default.ResultsFileLine) + "\n";
    }
}
