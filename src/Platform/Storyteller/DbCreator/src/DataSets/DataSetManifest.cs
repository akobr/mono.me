using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The set.json of a set. Member names match in any case, an unknown member is refused, so a typo never passes as a
// default. Organization, Project and View are the default target of the set (DataSetTarget).
public sealed record class DataSetManifest
{
    public const string FileName = "set.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // The name of the folder when set.json has none.
    public string Name { get; init; } = string.Empty;

    // A new version seeds the set again even when its files are the same.
    public int Version { get; init; } = 1;

    public string? Description { get; init; }

    public string? Organization { get; init; }

    public string? Project { get; init; }

    public string? View { get; init; }

    // The description of the project when the seed creates it.
    public string? ProjectDescription { get; init; }

    // The description of the view when the seed registers it.
    public string? ViewDescription { get; init; }

    // Sets of the catalog seeded before this one, into the same target.
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    // The manifest of the set in the folder; DataSetException when it is missing or not valid.
    public static DataSetManifest Read(IFileSystem fileSystem, string folder)
    {
        var path = fileSystem.Path.Combine(folder, FileName);

        if (!fileSystem.File.Exists(path))
        {
            throw new DataSetException($"The folder '{folder}' is not a set: it has no {FileName}.");
        }

        DataSetManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<DataSetManifest>(fileSystem.File.ReadAllText(path), Options);
        }
        catch (JsonException exception)
        {
            throw new DataSetException($"The manifest '{path}' is not valid: {exception.Message}", exception);
        }

        if (manifest is null)
        {
            throw new DataSetException($"The manifest '{path}' is not valid: it is not a JSON object.");
        }

        var name = string.IsNullOrWhiteSpace(manifest.Name)
            ? fileSystem.Path.GetFileName(fileSystem.Path.TrimEndingDirectorySeparator(folder))
            : manifest.Name.Trim();

        // the name is a segment of the id of the seed record and an argument of the command
        if (!NameRules.IsValidName(name))
        {
            throw new DataSetException(
                $"The set name '{name}' of '{path}' is not valid: use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit (Name in {FileName}, or the name of the folder).");
        }

        if (manifest.Version < 1)
        {
            throw new DataSetException($"The manifest '{path}' is not valid: Version must be 1 or more.");
        }

        var dependsOn = manifest.DependsOn ?? [];

        if (dependsOn.Any(string.IsNullOrWhiteSpace))
        {
            throw new DataSetException($"The manifest '{path}' is not valid: DependsOn has an empty name.");
        }

        return manifest with
        {
            Name = name,
            DependsOn = dependsOn.Select(dependency => dependency.Trim()).Distinct(StringComparer.Ordinal).ToList(),
            Organization = Trim(manifest.Organization),
            Project = Trim(manifest.Project),
            View = Trim(manifest.View),
        };
    }

    private static string? Trim(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
