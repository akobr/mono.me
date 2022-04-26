using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using c0ded0c.Core;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

using ProjectInfo = c0ded0c.Core.ProjectInfo;

namespace c0ded0c.MsBuild
{
    public class MsBuildProjectProcessingMiddleware : IProjectProcessingMiddleware, IInitialisableWithProperties
    {
        private readonly IPathCalculatorProvider pathCalculatorProvider;
        private readonly IIdentificationBuilder keyBuilder;
        private readonly ILogger<IProjectProcessingEngine> logger;

        private ImmutableDictionary<string, string> properties;

        public MsBuildProjectProcessingMiddleware(
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder,
            ILogger<IProjectProcessingEngine> logger)
        {
            this.pathCalculatorProvider = pathCalculatorProvider ?? throw new ArgumentNullException(nameof(pathCalculatorProvider));
            this.keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
            this.logger = logger;
            properties = ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
        }

        public void Initialise(IImmutableDictionary<string, string> configuration)
        {
            properties = properties.SetItems(configuration);

            // This loads the real MSBuild from the toolset so that all targets and SDKs can be found as if a real build is happening
            MSBuildLocator.RegisterDefaults();
        }

        public async Task<IProjectInfo> ProcessAsync(IProjectInfo project, ProjectProcessingAsyncDelegate next)
        {
            return await next(await ProcessAsync(project));
        }

        private async Task<IProjectInfo> ProcessAsync(IProjectInfo project)
        {
            string extension = Path.GetExtension(project.Path);

            if (extension != null)
            {
                extension = extension.ToLowerInvariant();
            }

            Workspace workspace;

            switch (extension)
            {
                case ".sln":
                    workspace = await OpenSolutionAsync(project.Path);
                    break;

                case ".csproj":
                    workspace = await OpenCSharpProjectAsync(project.Path);
                    break;

                // TODO: [P3] Add support for .dll, .winmd, .netmodule
                // Microsoft.SourceBrowser.HtmlGenerator.MetadataAsSource.LoadMetadataAsSourceSolution
                // TODO: [P4] Add support for .vbproj
                default:
                    return project;
            }

            return await LoadAssembliesAsync(project, workspace);
        }

        private async Task<IProjectInfo> LoadAssembliesAsync(IProjectInfo project, Workspace workspace)
        {
            if (workspace == null)
            {
                logger.LogError($"No workspace for project {project.Name}");
                return project;
            }

            ProjectInfo familiarProject = ProjectInfo.From(project);

            foreach (Project projectInSolution in workspace.CurrentSolution.Projects)
            {
                if (!string.Equals(projectInSolution.Language, LanguageNames.CSharp, StringComparison.OrdinalIgnoreCase)
                    || projectInSolution.FilePath == null)
                {
                    continue;
                }

                string projectPath = Path.GetFullPath(projectInSolution.FilePath);
                string projectFileName = Path.GetFileName(projectPath);
                IIdentificator? projectKey = IdentificationExtensions.BuildKeyOfProjectFromFilePath(projectInSolution.FilePath, pathCalculatorProvider, keyBuilder);

                if (familiarProject.Projects.TryGetValue(projectFileName, out IProjectInfo? subProject))
                {
                    ProjectInfo withAssembly = await CreateAssemblyAsync(ProjectInfo.From(subProject), projectInSolution);
                    familiarProject = familiarProject.SetProject(withAssembly);
                }
                else if (familiarProject.Key == projectKey)
                {
                    familiarProject = await CreateAssemblyAsync(familiarProject, projectInSolution);
                }
            }

            return familiarProject;
        }

        private async Task<ProjectInfo> CreateAssemblyAsync(ProjectInfo project, Project projectInSolution)
        {
            Compilation? compilation = await projectInSolution.GetCompilationAsync();

            if (compilation == null)
            {
                logger.LogError($"No compilation for project {project.Name}");
                return project;
            }

            var assembly = compilation.Assembly.BuildInfo(pathCalculatorProvider, keyBuilder);

            string? defaultNamespace = projectInSolution.DefaultNamespace;
            if (!string.IsNullOrEmpty(defaultNamespace))
            {
                assembly = assembly.SetExpansion(
                    Expansion.Empty.SetProperty(nameof(Project.DefaultNamespace), defaultNamespace));
            }
            else
            {
                logger.LogWarning($"No default namespace for project {project.Name}");
            }

            assembly.MutableTag = projectInSolution;
            return project.SetAssembly(assembly);
        }

        private async Task<Workspace> OpenSolutionAsync(string solutionPath)
        {
            var workspace = CreateMsBuildWorkspace(AddNecessarySolutionBuildProperties(properties, solutionPath));
            workspace.SkipUnrecognizedProjects = true;
            workspace.WorkspaceFailed += OnWorkspaceFailed;
            await workspace.OpenSolutionAsync(solutionPath);
            return workspace;
        }

        private async Task<Workspace> OpenCSharpProjectAsync(string projectPath)
        {
            var workspace = CreateMsBuildWorkspace(properties);
            workspace.WorkspaceFailed += OnWorkspaceFailed;
            await workspace.OpenProjectAsync(projectPath);
            return workspace;
        }

        private void OnWorkspaceFailed(object? sender, WorkspaceDiagnosticEventArgs e)
        {
            if (e?.Diagnostic?.Message == null)
            {
                return;
            }

            logger.LogWarning($"[Workspace {e.Diagnostic.Kind}] {e.Diagnostic.Message}");
        }

        private static MSBuildWorkspace CreateMsBuildWorkspace(ImmutableDictionary<string, string> properties)
        {
            var workspace = MSBuildWorkspace.Create(AddNecessaryVsBuildProperties(properties));
            workspace.LoadMetadataForReferencedProjects = true;
            return workspace;
        }

        private static ImmutableDictionary<string, string> AddNecessarySolutionBuildProperties(ImmutableDictionary<string, string> properties, string solutionFilePath)
        {
            var builder = properties.ToBuilder();

            // http://referencesource.microsoft.com/#MSBuildFiles/C/ProgramFiles(x86)/MSBuild/14.0/bin_/amd64/Microsoft.Common.CurrentVersion.targets,296
            builder["SolutionName"] = Path.GetFileNameWithoutExtension(solutionFilePath);
            builder["SolutionFileName"] = Path.GetFileName(solutionFilePath);
            builder["SolutionPath"] = solutionFilePath;
            builder["SolutionDir"] = Path.GetDirectoryName(solutionFilePath);
            builder["SolutionExt"] = Path.GetExtension(solutionFilePath);

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<string, string> AddNecessaryVsBuildProperties(ImmutableDictionary<string, string> properties)
        {
            var builder = properties.ToBuilder();

            // Explicitly add "CheckForSystemRuntimeDependency = true" property to correctly resolve facade references.
            // See https://github.com/dotnet/roslyn/issues/560
            builder["CheckForSystemRuntimeDependency"] = "true";
            builder["VisualStudioVersion"] = "16.0";
            builder["AlwaysCompileMarkupFilesInSeparateDomain"] = "false";

            return builder.ToImmutable();
        }

        private static string GetAssemblyFullName(IAssemblySymbol assembly)
        {
            string fullName = $"{assembly.Identity.Name},v.{assembly.Identity.Version}";

            if (assembly.Identity.HasPublicKey)
            {
                byte[] key = new byte[assembly.Identity.PublicKey.Length];
                assembly.Identity.PublicKey.CopyTo(key);
                fullName += $",{string.Concat(Array.ConvertAll(key, x => x.ToString("x2")))}";
            }
            else if (!assembly.Identity.PublicKeyToken.IsDefaultOrEmpty)
            {
                byte[] key = new byte[assembly.Identity.PublicKeyToken.Length];
                assembly.Identity.PublicKeyToken.CopyTo(key);
                fullName += $",{string.Concat(Array.ConvertAll(key, x => x.ToString("x2")))}";
            }

            return fullName;
        }
    }
}
