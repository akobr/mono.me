using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The project-settings.json at the root of a set: the settings of the target project (the names, colors and icons of
// its annotation types, its default time zone and its date format) in the file shape that sform project settings -e
// writes ("Format": "2s.project-settings.v1", Types, DefaultTimeZone, DateFormat). It is read with the rules of the API
// (ProjectSettingsJson.Read): member names and type codes in any case, Format optional, an unknown member refused;
// comments and trailing commas are allowed, as in set.json. Path is the full path of the file.
public sealed record class DataSetProjectSettings(string Path, ProjectSettings Settings)
{
    public const string FileName = "project-settings.json";

    // Far more than the largest valid file (seven types of four members, indented, with comments); the API takes 16 KiB.
    public const int MaxFileBytes = 64 * 1024;

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        AllowDuplicateProperties = false,
    };

    // The settings of the set in the folder, null without the file. DataSetException when the file is too large, is not
    // JSON or breaks a rule; the message names the file and the JSON path of every problem.
    public static DataSetProjectSettings? Read(IFileSystem fileSystem, string folder)
    {
        var path = fileSystem.Path.Combine(folder, FileName);

        if (!fileSystem.File.Exists(path))
        {
            return null;
        }

        if (fileSystem.FileInfo.New(path).Length > MaxFileBytes)
        {
            throw new DataSetException(
                $"The project settings '{path}' cannot be read: the file is larger than {MaxFileBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes.");
        }

        ProjectSettingsCheck check;

        try
        {
            check = ProjectSettingsJson.Read(JsonNode.Parse(fileSystem.File.ReadAllText(path), documentOptions: Options));
        }
        catch (JsonException exception)
        {
            throw new DataSetException($"The project settings '{path}' are not valid JSON: {exception.Message}", exception);
        }

        if (!check.IsValid)
        {
            var problems = string.Concat(check.Problems.Select(problem => $"\n  {problem.Path}: {problem.Message}"));
            var count = check.Problems.Count == 1 ? "1 problem" : $"{check.Problems.Count.ToString(CultureInfo.InvariantCulture)} problems";
            throw new DataSetException($"The project settings '{path}' are not valid ({count}):{problems}");
        }

        return new DataSetProjectSettings(path, check.Settings);
    }

    // The settings of a run, whose sets all seed into one project: those of the sets that have a project-settings.json,
    // which must hold the same values (ProjectSettingsRules.SameValues), or null when no set has one. DataSetException,
    // naming the first two sets that differ and their files, when they do not.
    public static DataSetProjectSettings? Agree(IReadOnlyList<(DataSet Set, DataSetProjectSettings Settings)> sets)
    {
        if (sets.Count == 0)
        {
            return null;
        }

        var (first, settings) = sets[0];
        var other = sets.Skip(1).FirstOrDefault(pair => !ProjectSettingsRules.SameValues(pair.Settings.Settings, settings.Settings));

        if (other.Set is not null)
        {
            throw new DataSetException(
                $"The sets '{first.Name}' and '{other.Set.Name}' carry different project settings ('{settings.Path}', '{other.Settings.Path}'), and they seed into one project, which has one set of settings: make the files equal, or seed the sets into different projects.");
        }

        return settings;
    }
}
