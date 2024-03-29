using System;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Items;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Show
{
    [Command(CommandNames.DEPENDENCY_TREE, Description = "Show dependency tree for a current location.")]
    public class ShowDependencyTreeCommand : BaseSourceCommand
    {
        public ShowDependencyTreeCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Option("-p|--packages", CommandOptionType.NoValue, Description = "Determining whether to include external dependencies to packages.")]
        public bool ShowPackages { get; }

        protected override async Task<int> ExecuteAsync()
        {
            Console.WriteHeader(
                ShowPackages ? "Dependency tree of " : "Internal dependency tree of ",
                Context.Item.Record.Identifier.Humanized.ThemedHighlight(Console.Theme));

            var tree = await BuildTreeAsync(Context.Item);

            if (tree.Children.Count < 1)
            {
                Console.WriteLine("Here is nothing to show.".ThemedLowlight(Console.Theme));
            }
            else
            {
                Console.WriteTree(tree, n => n);
            }

            return ExitCodes.SUCCESS;
        }

        private async Task<Composition> BuildTreeAsync(IItem item)
        {
            return item switch
            {
                IProject project => await BuildProjectNodeAsync(project),
                IWorkstead workstead => await BuildWorksteadNodeAsync(workstead),
                IRepository repository => await BuildRepositoryNodeAsync(repository),
                _ => throw new InvalidOperationException("Invalid item type."),
            };
        }

        private async Task<Composition> BuildRepositoryNodeAsync(IRepository repository)
        {
            var record = repository.Record;
            var node = new Composition($"{record.Name} [repository]".Lowlight());
            foreach (var subWorkstead in repository.GetWorksteads())
            {
                node.Children.Add(await BuildWorksteadNodeAsync(subWorkstead));
            }

            return node;
        }

        private async Task<Composition> BuildWorksteadNodeAsync(IWorkstead workstead)
        {
            var record = workstead.Record;
            var node = new Composition(new[] { record.Name, " [workstead]".Lowlight() });
            foreach (var subWorkstead in workstead.GetSubWorksteads())
            {
                node.Children.Add(await BuildWorksteadNodeAsync(subWorkstead));
            }

            foreach (var project in workstead.GetProjects())
            {
                node.Children.Add(await BuildProjectNodeAsync(project));
            }

            return node;
        }

        private async Task<Composition> BuildProjectNodeAsync(IProject project)
        {
            var record = project.Record;
            var repository = Context.Repository;
            var projectNode = new Composition(record.Name);
            await BuildProjectChildren(project, projectNode, 1, repository);
            return projectNode;
        }

        private async Task<Composition> BuildDependProjectNodeAsync(IProject project, int level)
        {
            var record = project.Record;
            var repository = Context.Repository;

            if (level > 10)
            {
                return new Composition(new[] { record.GetHierarchicalName(), " [max depth reached]".Lowlight() });
            }

            var projectNode = new Composition(record.GetHierarchicalName());
            await BuildProjectChildren(project, projectNode, level, repository);

            return projectNode;
        }

        private async Task BuildProjectChildren(IProject project, Composition projectNode, int level, IRepository repository)
        {
            var dependencies = await project.GetInternalDependenciesAsync();

            foreach (var dependency in dependencies)
            {
                var projectRecord = MonorepoDirectoryFunctions.GetRecord(dependency.FullPath);
                var projectItem = repository.TryGetDescendant(projectRecord);

                if (projectItem is IProject subProject)
                {
                    projectNode.Children.Add(await BuildDependProjectNodeAsync(subProject, level + 1));
                }
                else
                {
                    projectNode.Children.Add(new[] { projectRecord.Identifier.Humanized, " [unknown]".Lowlight() });
                }

                if (ShowPackages)
                {
                    var externalDependencies = await project.GetExternalDependenciesAsync();

                    if (externalDependencies.Count > 0)
                    {
                        foreach (var externalDependency in externalDependencies)
                        {
                            projectNode.Children.Add(new[]
                                { $"{externalDependency.Name} v.{externalDependency.Version}".Lowlight() });
                        }
                    }
                }
            }
        }
    }
}
