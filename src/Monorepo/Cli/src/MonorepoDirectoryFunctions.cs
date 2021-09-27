using System;
using System.IO;
using System.Linq;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli
{
    public static class MonorepoDirectoryFunctions
    {
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
            return GetMonorepoRootDirectory(Directory.GetCurrentDirectory());
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

        public static IItemRecord GetCurrentItem()
        {
            return GetItem(Directory.GetCurrentDirectory());
        }

        public static IItemRecord GetItem(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return new InvalidItemRecord(directoryPath);
            }

            RepositoryRecord repo = new(GetMonorepoRootDirectory(directoryPath));

            if (!repo.IsValid)
            {
                return new InvalidItemRecord(directoryPath);
            }

            string relativePath = directoryPath.GetRelativePath(repo.Path);
            string[] segments = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length < 2
                || !segments[0].EqualsOrdinalIgnoreCase(Constants.SOURCE_DIRECTORY_NAME))
            {
                return repo;
            }

            WorksteadRecord workstead = new(Path.Combine(repo.Path, Constants.SOURCE_DIRECTORY_NAME, segments[1]), repo);

            if (segments.Length < 3)
            {
                return workstead;
            }

            IItemRecord record = workstead;
            for (var i = 2; i < segments.Length; i++)
            {
                string directory = Path.Combine(record.Path, segments[i]);
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
            string name = Path.GetFileName(directory);
            return name[0] == '.' || name[0] == '_';
        }

        public static bool IsProjectDirectory(string directory)
        {
            return Directory.GetDirectories(directory, Constants.SOURCE_DIRECTORY_NAME).Any();
        }

        private static bool IsMonorepoRootDirectory(DirectoryInfo directory)
        {
            FileInfo[] files = directory.GetFiles(Constants.MONOREPO_CONFIG_JSON, SearchOption.TopDirectoryOnly);

            if (files.Length < 1)
            {
                return false;
            }

#if DEBUG
            return true;
#else
            return LibGit2Sharp.Repository.IsValid(directory);
#endif
        }
    }
}
