using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

using Prompt = Sharprompt.Prompt;

namespace _42.Monorepo.Cli.Commands.Init
{
    [Command(CommandNames.INIT, Description = "Initialise a new mono-repository.")]
    public class InitCommand : BaseCommand
    {
        public InitCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        protected override Task ExecutePreconditionsAsync()
        {
            return Task.CompletedTask;
        }

        protected override Task<int> ExecuteAsync()
        {
            if (Context.IsValid)
            {
                Console.WriteImportant("You are already in ", "mono-repository".ThemedHighlight(Console.Theme), ", hurray!");
                return Task.FromResult(ExitCodes.WARNING_NO_WORK_NEEDED);
            }

            if (!Console.Confirm("Do you want to create a new mono-repository in current folder"))
            {
                return Task.FromResult(ExitCodes.WARNING_ABORTED);
            }

            Console.WriteLine();
            Console.WriteHeader("Mono-repository tools");
            Console.WriteLine("Hi Earthling, now I'm going to ask you a couple of questions and try to explain to you what kind of tools you should use to simplify processes around managing a mono-repository.");

            Console.WriteLine();
            Console.WriteLine(
                "Very important to solve are problems of ",
                "(1) dependency management".ThemedHighlight(Console.Theme),
                ", ",
                "(2) versioning".ThemedHighlight(Console.Theme),
                " and ",
                "(3) build/CI tooling".ThemedHighlight(Console.Theme),
                ".");

            Console.WriteLine();
            Console.WriteHeader("(1) Dependency management");
            Console.WriteLine("Dependency management is a tricky problem but there are a couple of handy possibilities how to centralized the management for entire repository or project groups.");
            Console.WriteLine(
                "I recommend you to use ",
                "Central package version management".ThemedHighlight(Console.Theme),
                " even when it is still in preview, because this is the simple out-of-the-box solution which can be easily changed to any other strategy.");

            SelectOptions<Feature> featureOptions = new()
            {
                Items = new List<Feature>
                {
                    // Use a smart versioning based on git history and allow to generate release logs from conventional commit messages.
                    // The Microsoft.Build.Traversal msbuild SDK is used to have two different views of repository projects. The first one is a human/developer point of view with solution and their filter files per each workstead. Second one is used for machines/tooling defined by Directory.Build.proj, which allow to have custom build per any point/directory of the mono-repository.

                    new("CentralPackageVersionsSdk",
                        "External MsBuild SDK",
                        "A custom MsBuild SDK built by NuGet team, named as Microsoft.Build.CentralPackageVersions. SHORT_LINK"),
                    new("CentralPackageVersionManagement",
                        "Central package version management [Preview]",
                        "A baked in solution into .NET Core SDK (from 3.1.300), out-of-the-box solution with Directory.Packages.props file, but still a preview feature. SHORT_LINK"),
                    new("Paket",
                        "Paket package manager",
                        "An alternative package manager to NuGet for .NET projects, which has some great features. SHORT_LINK"),
                },
                Message = "Please pickup which dependency management you want to use",
                TextSelector = f => f.Name,
            };

            featureOptions.DefaultValue = featureOptions.Items.Skip(1).First();
            Console.WriteLine();
            var featureDependencies = Console.Select(featureOptions);

            Console.WriteLine();
            Console.WriteHeader("(2) Versioning");
            Console.WriteLine("If you are lucky and all projects are just internally used inside your mono-repository you don't need to care about versioning and everything will be just project references.");
            Console.WriteLine("More scary case is when you need to serve multiple libraries as NuGet packages or through any other packing system, then you should manage multiple versions and independent releases.");
            Console.WriteLine("Don't be scared even for this approach I put together a couple of tooling as part of this toolset.");
            Console.WriteLine();
            Console.WriteLine("Mine versioning system is using git history as the only source of the truth. It is based on NerdBank.GetVersioning library which has been slightly updated to a mono-repository needs. SHORT_LINK");
            Console.WriteLine("To simplify releases and their notes I recommend you to use conventional commits and our CLI tools for releasing. SHORT_LINK");

            // TODO
            Console.Confirm("Do you want to turn on CommitLint for conventional messages");

            Console.WriteLine();
            Console.WriteHeader("(3) Build and CI tooling");
            Console.WriteLine("");

            var doc = new Document(
                new Span("# Feature list:"),
                new Grid
                {
                    Color = ConsoleColor.Gray,
                    Stroke = LineThickness.None,
                    Margin = new Thickness(2, 0),
                    Columns = { GridLength.Auto, GridLength.Star(1) },
                    Children =
                    {
                        new Cell("Feature") { Stroke = LineThickness.None },
                        new Cell("Description") { Stroke = LineThickness.None },
                        new Cell(new Separator()) { ColumnSpan = 2, Stroke = LineThickness.None },
                        featureOptions.Items.Select(f => new[]
                        {
                            new Cell($"> {f.Name}  ") { Stroke = LineThickness.None },
                            new Cell(f.Description) { Stroke = LineThickness.None, Padding = new Thickness(0, 0, 0, 1)},
                        }),
                    },
                });

            Console.WriteExactDocument(doc);
            Console.WriteLine();

            Console.WriteImportant("This command is still under development...");
            return Task.FromResult(ExitCodes.SUCCESS);
        }
    }
}
