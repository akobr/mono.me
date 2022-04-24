using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace _42.Monorepo.Repo.Generator;

internal class MsBuild : IDisposable
{
    private static readonly SemaphoreSlim _buildSemaphore = new(1, 1);
    private readonly string _solutionFilePath;
    private Workspace? _workspace;

    public MsBuild(string solutionFilePath)
    {
        _solutionFilePath = solutionFilePath;
        Projects = Array.Empty<Project>();
    }

    public IReadOnlyList<Project> Projects { get; private set; }

    public async Task InitialiseAsync()
    {
        if (_workspace is not null)
        {
            return;
        }

        await _buildSemaphore.WaitAsync();

        try
        {
            _workspace = await OpenSolutionAsync(_solutionFilePath);
            Projects = _workspace.CurrentSolution.Projects.ToList();
        }
        finally
        {
            _buildSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }

    private async Task<Workspace> OpenSolutionAsync(string solutionPath)
    {
        var workspace = CreateMsBuildWorkspace(AddNecessarySolutionBuildProperties(
            ImmutableDictionary<string, string>.Empty,
            solutionPath));
        workspace.SkipUnrecognizedProjects = true;
        workspace.WorkspaceFailed += OnWorkspaceFailed;
        await workspace.OpenSolutionAsync(solutionPath);
        return workspace;
    }

    private void OnWorkspaceFailed(object? sender, WorkspaceDiagnosticEventArgs e)
    {
        if (e?.Diagnostic?.Message == null)
        {
            return;
        }

        Console.WriteLine($"[Workspace {e.Diagnostic.Kind}] {e.Diagnostic.Message}");
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
}
