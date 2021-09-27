using System;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Output;
using Alba.CsConsoleFormat.Fluent;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command("info", Description = "Display info of a location in the mono-repository.")]
    public class InfoCommand : BaseCommand
    {
        public InfoCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        protected override async Task ExecuteAsync()
        {
            var pLocation = Context.Item;
            var location = pLocation.Record;

            Console.WriteHeader($"{Enum.GetName(location.Type)}: ", location.Name.Magenta(), $" v.{await pLocation.TryGetDefinedVersionAsync()}");
            Console.WriteLine($"Path: {location.Path.GetRelativePath(Context.Repository.Record.Path)}".DarkGray());

            if (pLocation is IProject project)
            {
                var internalDependencies = await project.GetInternalDependenciesAsync();

                if (internalDependencies.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteHeader("Project references:");
                    Console.WriteTable(
                        internalDependencies,
                        d => new[] { $" > {d.Name}", d.Path.GetRelativePath(Context.Repository.Record.Path) },
                        new[] { "Project", "Repository path" });
                }
            }

            var externalDependencies = await pLocation.GetExternalDependenciesAsync();

            if (externalDependencies.Count > 0)
            {
                Console.WriteLine();
                Console.WriteHeader("Package references:");
                Console.WriteTable(
                    externalDependencies,
                    d => new[] { $" > {d.Name}", d.Version },
                    new[] { "Package", "Version" });
            }
        }
    }
}
