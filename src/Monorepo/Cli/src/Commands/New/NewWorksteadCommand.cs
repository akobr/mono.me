using System;
using System.IO;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Output;
using Alba.CsConsoleFormat.Fluent;
using McMaster.Extensions.CommandLineUtils;

using Prompt = Sharprompt.Prompt;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command("workstead")]
    public class NewWorksteadCommand : BaseCommand
    {
        public NewWorksteadCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Argument(0, Description = "Name of the workstead.")]
        public string? Name { get; } = string.Empty;

        protected override Task ExecuteAsync()
        {
            var targetItem = Context.Item;

            if (Context.Item.Record.Type > ItemType.Workstead)
            {
                Console.WriteLine("A workstead should not be created under a project.".DarkGray());

                if (!Console.Confirm("Do you want to create a new top level workstead, instead"))
                {
                    return Task.CompletedTask;
                }

                targetItem = Context.Repository;
            }

            var name = Name;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Prompt.Input<string>("Please give me a name for the new workstead");

                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException("The name of the workstead needs to be specified.");
                }
            }

            var path = Path.Combine(targetItem.Record.Path, Constants.SOURCE_DIRECTORY_NAME, name);

            if (Directory.Exists(path))
            {
                throw new InvalidOperationException($"The workstead '{name}' already exists.");
            }

#if !DEBUG
            Directory.CreateDirectory(path);
#endif
            Console.WriteLine("The workstead '", name.Magenta(), "' has been created.");
            Console.WriteLine($"Path: {path}".DarkGray());
            return Task.CompletedTask;
        }
    }
}
