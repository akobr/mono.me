using System;
using System.IO;
using System.Linq;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli
{
    public static class MonorepoDirectoryFunctions
    {
        private static string? _monorepoRootDirectory;

        public static bool IsMonoRepository()
        {
            return IsMonoRepository(Directory.GetCurrentDirectory());
        }

        public static bool IsMonoRepository(string directoryPath)
        {
            var directory = new DirectoryInfo(directoryPath);

            while (directory is { Exists: true })
            {
                if (IsMonorepoRootDirectory(directory))
                {
                    return true;
                }

                directory = directory.Parent;
            }

            return false;
        }

        public static string GetMonorepoRootDirectory()
        {
            if (_monorepoRootDirectory is not null)
            {
                return _monorepoRootDirectory;
            }

            return _monorepoRootDirectory = GetMonorepoRootDirectory(Directory.GetCurrentDirectory());
        }

        public static string GetMonorepoRootDirectory(string directoryPath)
        {
            var directory = new DirectoryInfo(directoryPath);

            while (directory is { Exists: true })
            {
                if (IsMonorepoRootDirectory(directory))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return Directory.GetCurrentDirectory();
        }

        public static RepositoryRecord GetMonoRepository()
        {
            return new RepositoryRecord(GetMonorepoRootDirectory());
        }

        public static IRecord GetCurrentRecord(IItemOptionsProvider? optionsProvider = null)
        {
            return GetRecord(Directory.GetCurrentDirectory(), optionsProvider);
        }

        public static IRecord GetRecord(string anyPathProcessedAsAbsolute, IItemOptionsProvider? optionsProvider = null)
        {
            var path = anyPathProcessedAsAbsolute;

            if (File.Exists(path))
            {
                path = Path.GetDirectoryName(path)!;
            }

            if (!Directory.Exists(path))
            {
                return new InvalidRecord(path);
            }

            RepositoryRecord repo = new(GetMonorepoRootDirectory(path));

            if (!repo.IsValid)
            {
                return new InvalidRecord(path);
            }

            var relativePath = path.GetRelativePath(repo.Path);
            var segments = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length < 2
                || !segments[0].EqualsOrdinalIgnoreCase(Constants.SOURCE_DIRECTORY_NAME))
            {
                if (optionsProvider?.TryGetOptions(relativePath, out _) ?? false)
                {
                    return new SpecialRecord(Path.Combine(repo.Path, relativePath), repo);
                }

                return repo;
            }

            WorksteadRecord workstead = new(Path.Combine(repo.Path, Constants.SOURCE_DIRECTORY_NAME, segments[1]), repo);

            if (segments.Length < 3)
            {
                return workstead;
            }

            IRecord record = workstead;
            for (var i = 2; i < segments.Length; i++)
            {
                var directory = Path.Combine(record.Path, segments[i]);
                if (IsProjectDirectory(directory))
                {
                    record = new ProjectRecord(directory, record);
                    break;
                }

                record = new WorksteadRecord(directory, record);
            }

            return record;
        }

        public static bool IsExcludedDirectory(string directory)
        {
            var name = Path.GetFileName(directory);
            return name[0] == '.' || name == "obj" || name == "bin"; // TODO [P3]: make this more robust
        }

        public static bool IsProjectDirectory(string directory)
        {
            return Directory.GetDirectories(directory, Constants.SOURCE_DIRECTORY_NAME, SearchOption.TopDirectoryOnly).Any()
                   || Directory.GetFiles(directory, "*.*?proj", SearchOption.TopDirectoryOnly).Any();
        }

        private static bool IsMonorepoRootDirectory(DirectoryInfo directory)
        {
            var files = directory.GetFiles(Constants.MONOREPO_CONFIG_JSON, SearchOption.TopDirectoryOnly);

            if (files.Length < 1)
            {
                return false;
            }

#if DEBUG
            return true;
#else
            return LibGit2Sharp.Repository.IsValid(directory.FullName);
#endif
        }
    }
}
