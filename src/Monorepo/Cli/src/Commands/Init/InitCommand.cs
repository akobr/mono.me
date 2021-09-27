using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;
using Alba.CsConsoleFormat;
using Alba.CsConsoleFormat.Fluent;
using Sharprompt;

namespace _42.Monorepo.Cli.Commands.Init
{
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

        protected override Task ExecuteAsync()
        {
            if (Context.IsValid)
            {
                Console.WriteLine("You are already in ", "mono-repository".Magenta(), ", hurray!");
                return Task.CompletedTask;
            }

            if (!Console.Confirm("Do you want to create a new mono-repository in current folder"))
            {
                return Task.CompletedTask;
            }

            Console.WriteLine();
            Console.WriteLine(
                "We are recommending to use a couple of ",
                "independent tools to simplify versioning and management of the mono-repository".Magenta(),
                ". Please pick which one you want to use. ",
                "(All of them can be added any time.)".DarkGray());
            Console.WriteLine();
            Console.WriteLine();

            MultiSelectOptions<Feature> featureOptions = new()
            {
                Items = new List<Feature>
                {
                    new("git-versioning",
                        "git versioning",
                        "Use a smart versioning based on git history and allow to generate release logs from conventional commit messages."),
                    new("msbuild-sdk-central-package-versions",
                        "central package versions",
                        "Use Microsoft.Build.CentralPackageVersions msbuild SDK to simplify management of NuGet package dependencies. It allow you to store and manage package versions in centralised place."),
                    new("msbuild-sdk-traversal",
                        "traversal SDK",
                        "The Microsoft.Build.Traversal msbuild SDK is used to have two different views of repository projects. The first one is a human point of view with solution and filter files per each workstead. Second one is used for machines/tooling defined by Directory.Build.proj, which allow to build any point/directory of the mono-repository."),
                },
                Message = "Please pickup which features you want to add",
                TextSelector = f => f.Name,
            };

            featureOptions.DefaultValues = featureOptions.Items;

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
            IReadOnlyCollection<string> featuresToInstall = Console.MultiSelect(featureOptions)
                .Select(f => f.Id)
                .ToList();

            return Task.CompletedTask;
        }
    }
}
