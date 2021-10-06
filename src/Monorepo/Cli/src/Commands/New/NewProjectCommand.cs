using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command("project")]
    public class NewProjectCommand : BaseCommand
    {
        public NewProjectCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Argument(0, Description = "Name of the project.")]
        public string? Name { get; } = string.Empty;

        protected override Task ExecuteAsync()
        {
            var workstead = Context.Item.TryGetConcreteItem(ItemType.Workstead);

            if (workstead == null)
            {
                // TODO: interactive way to pick a workstead
                throw new NotImplementedException();
            }

            var name = Name;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Console.Input<string>("Please give me a name for the project");

                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException("The name of the project needs to be specified.");
                }
            }

            string path = Path.Combine(workstead.Record.Path, name);

            if (Directory.Exists(path))
            {
                throw new InvalidOperationException($"The project '{name}' already exists.");
            }

#if !DEBUG
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(Path.Combine(path, Constants.SOURCE_DIRECTORY_NAME));
            Directory.CreateDirectory(Path.Combine(path, Constants.TEST_DIRECTORY_NAME));

            string projectSrcFileName = Path.Combine(path, Constants.SOURCE_DIRECTORY_NAME, $"{name}.csproj");
            string projectTestFileName = Path.Combine(path, Constants.SOURCE_DIRECTORY_NAME, $"{name}.Test.csproj");

            if (!File.Exists(projectSrcFileName))
            {
                using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Library.csproj");
                using var target = File.Open(projectSrcFileName, FileMode.Create, FileAccess.Write);
                source?.CopyTo(target);
            }

            if (!File.Exists(projectTestFileName))
            {
                using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Library.Test.csproj");
                using var target = File.Open(projectSrcFileName, FileMode.Create, FileAccess.Write);
                source?.CopyTo(target);
            }
#endif

            Console.WriteLine();
            Console.WriteImportant("The project '", name.ThemedHighlight(Console.Theme), "' has been created.");
            Console.WriteLine($"Directory: {path}".ThemedLowlight(Console.Theme));
            Console.WriteLine($"Project: {Path.Combine(path, Constants.SOURCE_DIRECTORY_NAME, $"{name}.csproj")}".ThemedLowlight(Console.Theme));
            return Task.CompletedTask;
        }
    }
}
