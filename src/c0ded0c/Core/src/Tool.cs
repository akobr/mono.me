using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace c0ded0c.Core
{
    public class Tool : ITool
    {
        private readonly IMechanism mechanism;
        private readonly ILogger<ITool> logger;

        public Tool(IMechanism mechanism, ILogger<ITool>? logger)
        {
            this.mechanism = mechanism ?? throw new ArgumentNullException(nameof(mechanism));
            this.logger = logger;
        }

        // TODO: should return IReadOnlyToolContext
        public async Task BuildAsync(IProgress<IToolProgress>? observer = null, CancellationToken cancellation = default)
        {
            WorkspaceInfo.Builder workspace = WorkspaceInfo.CreateEmpty("test").ToBuilder();
            var projects = await mechanism.Mining.MineAsync(cancellation);
            observer.Report($"{projects.Count} projects have been found to document...");

            // Must be split to multiple core
            foreach (IProjectInfo project in projects.OrderBy(p => p.IsAggregated))
            {
                observer.Report($"Processing of {project.Name} has been started.");
                IProjectInfo loadedProject = await mechanism.ProjectPreceeding.ProcessAsync(project, cancellation);
                observer.Report("The project processing is done.");

                if (loadedProject.IsAggregated)
                {
                    foreach (IProjectInfo subProject in loadedProject.Projects.Values)
                    {
                        TryToAddToWorkspace(
                            workspace,
                            await ProcessAssemblyAsync(subProject, observer, cancellation));
                    }

                    if (loadedProject.Assembly != null)
                    {
                        TryToAddToWorkspace(
                            workspace,
                            await ProcessAssemblyAsync(loadedProject, observer, cancellation));
                    }
                }
                else
                {
                    TryToAddToWorkspace(
                        workspace,
                        await ProcessAssemblyAsync(loadedProject, observer, cancellation));
                }
            }

            await mechanism.Genesis.ShapeAsync(workspace.ToImmutable());
        }

        private void TryToAddToWorkspace(WorkspaceInfo.Builder workspace, IAssemblyInfo? assembly)
        {
            if (assembly == null)
            {
                return;
            }

            workspace.AddAssembly(assembly);
        }

        private async Task<IAssemblyInfo?> ProcessAssemblyAsync(IProjectInfo project, IProgress<IToolProgress>? observer = null, CancellationToken cancellation = default)
        {
            if (project.Assembly == null)
            {
                logger.LogError($"The project {project.Name} is without assembly.");
                observer.Report("The project is without assembly.");
                return null;
            }

            observer.Report($"Processing of the assembly {project.Assembly.Name} has been started.");
            IAssemblyInfo processedAssembly = await mechanism.AssemblyPreceeding.ProcessAsync(project.Assembly, cancellation);
            observer.Report("The assembly processing is done.");
            return processedAssembly;
        }
    }
}
