using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using c0ded0c.Core;
using c0ded0c.Core.Mining;
using Microsoft.Build.Construction;
using Microsoft.Extensions.Logging;

namespace c0ded0c.MsBuild
{
    public class MsBuildMiningMiddleware : IMiningMiddleware
    {
        private readonly IPathCalculatorProvider pathCalculationProvider;
        private readonly IIdentificationBuilder keyBuilder;
        private readonly ILogger<IMiningEngine> logger;
        private readonly ISet<string> supportedExtensions;

        public MsBuildMiningMiddleware(
            IPathCalculatorProvider pathCalculationProvider,
            IIdentificationBuilder keyBuilder,
            ILogger<IMiningEngine> logger)
        {
            this.pathCalculationProvider = pathCalculationProvider ?? throw new ArgumentNullException(nameof(pathCalculationProvider));
            this.keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
            this.logger = logger;

            supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                FileExtensions.SOLUTION, FileExtensions.CSHARP_PROJECT,
            };
        }

        public async Task<IImmutableSet<IProjectInfo>> MineAsync(string path, MiningAsyncDelegate next)
        {
            if (File.Exists(path))
            {
                IProjectInfo? project = BuildProjectInfo(path);

                if (project != null)
                {
                    return ImmutableHashSet<IProjectInfo>.Empty.Add(project);
                }
                else
                {
                    return await next(path);
                }
            }
            else if (Directory.Exists(path))
            {
                return ProcessDirectory(path).Union(await next(path));
            }

            return ImmutableHashSet<IProjectInfo>.Empty;
        }

        private ImmutableHashSet<IProjectInfo> ProcessDirectory(string path)
        {
            ImmutableHashSet<IProjectInfo> projects = ImmutableHashSet<IProjectInfo>.Empty;

            foreach (string extension in supportedExtensions)
            {
                foreach (string filePath in Directory.GetFiles(path, $"*{extension}", SearchOption.AllDirectories))
                {
                    IProjectInfo? project = BuildProjectInfo(filePath);

                    if (project != null)
                    {
                        projects.Add(project);
                    }
                }
            }

            return projects;
        }

        private IProjectInfo? BuildProjectInfo(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string extension = Path.GetExtension(path);
            if (!supportedExtensions.Contains(extension))
            {
                return null;
            }

            string fileName = Path.GetFileName(path);
            logger.LogTrace($"Project {fileName} added.");

            ProjectInfo? project = InfoExtensions.BuildInfoOfProjectFromFilePath(path, pathCalculationProvider, keyBuilder);

            if (project == null)
            {
                return null;
            }

            if (string.Equals(extension, FileExtensions.SOLUTION, StringComparison.OrdinalIgnoreCase))
            {
                project = ReadSolutionProjects(project);
            }

            return project;
        }

        private ProjectInfo ReadSolutionProjects(ProjectInfo solutionInfo)
        {
            SolutionFile solution = SolutionFile.Parse(solutionInfo.Path);
            ProjectInfo.Builder solutionBuilder = solutionInfo.ToBuilder();

            foreach (ProjectInSolution project in solution.ProjectsInOrder)
            {
                if (project.ProjectType == SolutionProjectType.SolutionFolder)
                {
                    continue;
                }

                string projectFullPath = project.AbsolutePath;
                string extension = Path.GetExtension(projectFullPath);

                if (!string.Equals(extension, FileExtensions.CSHARP_PROJECT, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                solutionBuilder.AddProject(project.BuildInfo(pathCalculationProvider, keyBuilder));
            }

            return solutionBuilder.ToImmutable();
        }
    }
}
