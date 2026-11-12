using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The sets a run can seed: the built-in ones in data-sets/<name>/ next to the tool (copied to the output and the tool
// package), and those of the folders given to the command. Every file is read through the given file system.
public sealed class DataSetCatalog
{
    public const string BuiltInFolderName = "data-sets";

    private readonly IFileSystem _fileSystem;

    // baseDirectory: where the tool runs from, AppContext.BaseDirectory unless a test gives another
    public DataSetCatalog(IFileSystem fileSystem, string? baseDirectory = null)
    {
        _fileSystem = fileSystem;
        BuiltInFolder = fileSystem.Path.Combine(baseDirectory ?? AppContext.BaseDirectory, BuiltInFolderName);
    }

    public string BuiltInFolder { get; }

    // The built-in sets (by folder name) and the sets of the folders in the given order: a folder with set.json is one
    // set, otherwise each of its sub-folders with set.json is one. DataSetException for a folder that is missing or
    // holds no set, an invalid manifest, or a name found twice.
    public IReadOnlyList<DataSet> GetSets(IEnumerable<string>? paths = null)
    {
        var sets = new List<DataSet>();

        if (_fileSystem.Directory.Exists(BuiltInFolder))
        {
            sets.AddRange(GetSubFolderSets(BuiltInFolder).Select(folder => Read(folder, DataSetSource.BuiltIn)));
        }

        foreach (var path in paths ?? [])
        {
            var folder = _fileSystem.Path.GetFullPath(path);

            if (!_fileSystem.Directory.Exists(folder))
            {
                throw new DataSetException($"The folder '{path}' does not exist.");
            }

            if (IsSet(folder))
            {
                sets.Add(Read(folder, DataSetSource.Path));
                continue;
            }

            var folders = GetSubFolderSets(folder);

            if (folders.Count == 0)
            {
                throw new DataSetException($"The folder '{path}' holds no set: it has no {DataSetManifest.FileName}, and none of its folders has one.");
            }

            sets.AddRange(folders.Select(subFolder => Read(subFolder, DataSetSource.Path)));
        }

        var duplicate = sets
            .GroupBy(set => set.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new DataSetException(
                $"The set name '{duplicate.Key}' is found twice: in {string.Join(" and ", duplicate.Select(set => $"'{set.Folder}'"))}.");
        }

        return sets;
    }

    // The named sets and what they depend on, each once, every set after its dependencies; the names in the given
    // order otherwise. DataSetException for a name not in the catalog and for a cycle.
    public static IReadOnlyList<DataSet> Resolve(IReadOnlyList<DataSet> sets, IEnumerable<string> names)
    {
        var byName = sets.ToDictionary(set => set.Name, StringComparer.Ordinal);
        var ordered = new List<DataSet>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var name in names)
        {
            Visit(name.Trim(), null);
        }

        return ordered;

        void Visit(string name, string? dependent)
        {
            if (done.Contains(name))
            {
                return;
            }

            var cycle = path.IndexOf(name);

            if (cycle >= 0)
            {
                throw new DataSetException($"The sets depend on each other in a cycle: {string.Join(" -> ", path.Skip(cycle).Append(name))}.");
            }

            if (!byName.TryGetValue(name, out var set))
            {
                var known = byName.Count == 0 ? "it has no sets" : $"it has {string.Join(", ", byName.Keys.Order(StringComparer.Ordinal))}";
                throw new DataSetException(dependent is null
                    ? $"The set '{name}' is not in the catalog ({known})."
                    : $"The set '{dependent}' depends on '{name}', which is not in the catalog ({known}).");
            }

            path.Add(name);

            foreach (var dependency in set.Manifest.DependsOn)
            {
                Visit(dependency, name);
            }

            path.RemoveAt(path.Count - 1);
            done.Add(name);
            ordered.Add(set);
        }
    }

    private bool IsSet(string folder)
    {
        return _fileSystem.File.Exists(_fileSystem.Path.Combine(folder, DataSetManifest.FileName));
    }

    // The sub-folders with set.json, hidden ones left out, in ordinal order.
    private List<string> GetSubFolderSets(string folder)
    {
        return _fileSystem.Directory.EnumerateDirectories(folder)
            .Where(subFolder => !ViewArchiveNames.IsSkippedPath(_fileSystem.Path.GetFileName(subFolder)) && IsSet(subFolder))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private DataSet Read(string folder, DataSetSource source)
    {
        return new DataSet(DataSetManifest.Read(_fileSystem, folder), folder, source);
    }
}
