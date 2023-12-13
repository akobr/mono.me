using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Features;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Templates;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.REPOSITORY, CommandNames.REPO, Description = "Create new mono-repository.")]
    public class NewRepositoryCommand : BaseCommand
    {
        private readonly IFileSystem _fileSystem;

        public NewRepositoryCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context)
            : base(console, context)
        {
            _fileSystem = fileSystem;
        }

        protected override Task ExecutePreconditionsAsync()
        {
            return Task.CompletedTask;
        }

        protected override async Task<int> ExecuteAsync()
        {
#if !DEBUG
            if (Context.IsValid)
            {
                Console.WriteImportant("You are already in a ", "mono-repository".ThemedHighlight(Console.Theme), ", hurray!");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }
#endif

            if (!Console.Confirm("Do you want to create a new .net mono-repository in the current folder"))
            {
                return ExitCodes.WARNING_ABORTED;
            }

            Console.WriteLine();
            Console.WriteHeader("Mono-repository tools");
            Console.WriteLine("Hi Earthling, now I'm going to ask you a couple of questions and explain to you what kind of tools you should use to simplify processes around managing a mono-repository.");

            Console.WriteLine();
            Console.WriteLine(
                "Very important to solve are problems of ",
                "(1) dependency management".ThemedHighlight(Console.Theme),
                ", ",
                "(2) versioning".ThemedHighlight(Console.Theme),
                ", ",
                "(3) build/CI tooling".ThemedHighlight(Console.Theme),
                ", and ",
                "(4) coding standards".ThemedHighlight(Console.Theme),
                ".");

            Console.WriteLine();
            Console.WriteHeader("(1) Dependency management");
            Console.WriteLine("Dependency management is a tricky problem, but there are a couple of handy possibilities for centralizing the management for the entire repository or project groups.");
            Console.WriteLine(
                "I recommend you to use ",
                "Central package version management".ThemedHighlight(Console.Theme),
                " because this is the out-of-the-box solution in .net SDK, which is easily changeable to any other strategy.");

            SelectOptions<Feature> featureOptions = new()
            {
                Items = new List<Feature>
                {
                    new("CentralPackageVersionManagement",
                        "Central package version management",
                        "Recommended. A baked in solution into .NET Core SDK (from 3.1.300), using Directory.Packages.props file. https://bit.ly/3oKJCpq"),
                    new("DirectoryBuildTargets",
                        "Directory.Build.props [MsBuild 15+]",
                        "Use of hierarchical Directory.Build.props and the possibility to update version of package reference by MsBuild 15 and newer."),
                    new("Paket",
                        "Paket package manager",
                        "An alternative package manager for NuGet and .NET projects, which has some great features. Currently not yet supported by my tooling. https://bit.ly/3oHTJLp"),
                },
                Message = "Please pickup which dependency management you want to use",
                TextSelector = f => f.Name,
            };

            var doc = new Document(
                new Grid
                {
                    Color = ConsoleColor.Gray,
                    Stroke = LineThickness.None,
                    Margin = new Thickness(2, 0),
                    Columns = { GridLength.Auto, GridLength.Star(1) },
                    Children =
                    {
                        new Cell("Possible solution") { Stroke = LineThickness.None },
                        new Cell("Description") { Stroke = LineThickness.None },
                        new Cell(new Separator()) { ColumnSpan = 2, Stroke = LineThickness.None },
                        featureOptions.Items.Select(f => new[]
                        {
                            new Cell($"> {f.Name}  ") { Stroke = LineThickness.None },
                            new Cell(f.Description) { Stroke = LineThickness.None, Padding = new Thickness(0, 0, 0, 1) },
                        }),
                    },
                });

            Console.WriteLine();
            Console.WriteExactDocument(doc);
            Console.WriteLine();

            var useCentralDependencies = Console.Confirm("Should I turn on the central package version management", true);
            Console.WriteLine();

            if (useCentralDependencies)
            {
                Console.WriteLine("Detailed information about central package version management:");
                Console.WriteLine("    https://bit.ly/3tdIwI5");
            }
            else
            {
                Console.WriteLine("All dependencies are managed independently inside each project file. The advantages of the centralized approach are described at:");
                Console.WriteLine("    https://bit.ly/3TjSQc5");
            }

            Console.WriteLine();
            Console.WriteHeader("(2) Versioning");
            Console.WriteLine("If you are lucky and all projects are used only internally inside your mono-repository, you don't need to care about versioning, and everything will be just a project reference.");
            Console.WriteLine("A more complex case is when you need to serve multiple libraries as NuGet packages or through any other packing system, then you should manage numerous versions and independent releases.");
            Console.WriteLine("Don't be scared too much, even for this approach I put together a couple of tooling and recommendations as part of the toolset.");
            Console.WriteLine();
            Console.WriteLine(
                "Mine versioning system uses git history as the only source of the truth and a version. It is based on ",
                "NerdBank.GetVersioning".ThemedHighlight(Console.Theme),
                " library, which has been slightly updated to a mono-repository needs. https://bit.ly/3sAjIpy");
            Console.WriteLine(
                "The principle is simple; the monorepo contains one or multiple ",
                "version.json".ThemedLowlight(Console.Theme),
                " files which define and control version(s) for projects inside the subtree where every single commit can be built and produce a unique version.");

            Console.WriteLine();
            var useVersioning = Console.Confirm("Configure and use mentioned versioning system", true);
            Console.WriteLine();

            if (useVersioning)
            {
                Console.WriteLine("Detailed information about the versioning:");
                Console.WriteLine("    https://bit.ly/34Vsp5X");
            }
            else
            {
                Console.WriteLine("Guide how to configure versions manually in .net project:");
                Console.WriteLine("    https://bit.ly/3wlSfLK");
            }

            Console.WriteLine();
            Console.WriteHeader("(3) Build and CI tooling");
            Console.WriteLine("The first recommendation is to separate the human and machine view of the codebase. The solution file(s) should be used only by developers and never by any machine or a bot.");
            Console.WriteLine(
                "The ",
                "Microsoft.Build.Traversal".ThemedHighlight(Console.Theme),
                " msbuild SDK is used to serve the second view for machines. The magic is done by ",
                "Directory.Build.proj".ThemedLowlight(Console.Theme),
                " files allowing to build per any point/directory of the mono-repository. https://bit.ly/3sJl7tQ");
            Console.WriteLine("By default, my CLI tooling uses this system to build through it; if you pick to don't use it, you have to write your scripts for building.");

            Console.WriteLine();
            var useCodeViewSeparation = Console.Confirm("Turn it on and automatically create Directory.Build.proj file with each workstead", true);
            Console.WriteLine();

            if (useCodeViewSeparation)
            {
                Console.WriteLine("My idea of how to build from the mono-repository is described here:");
                Console.WriteLine("    https://bit.ly/3NhhN43");
                Console.WriteLine();
            }

            Console.WriteLine("To simplify releases and their notes, I recommend starting with conventional commits. https://bit.ly/3JsPtHY");
            Console.WriteLine("To push it even one step further, you should set up CommitLint to force any developer to have clean and friendly git history ready for automated releases. https://bit.ly/3rLCAm7");
            Console.WriteLine(
                "With all the above prerequisites and my CLI tools for releasing (",
                "mrepo release".ThemedLowlight(Console.Theme),
                "), the preparation and creation of release notes will be just a piece of cake.");

            Console.WriteLine();
            var useCommitLint = Console.Confirm("Do you want to turn on CommitLint for conventional messages", true);
            Console.WriteLine();

            if (useCommitLint)
            {
                Console.WriteLine("I will help you with the configuration of the CommitLint, but the installation needs to be done manually.");
                Console.WriteLine("You should set up node.js with npm, CommitLint, and Husky to force the rules on each commit:");
                Console.WriteLine();
                Console.WriteLine("    # install node.js by Chocolatey".ThemedLowlight(Console.Theme));
                Console.WriteLine("    choco install nodejs-lts");
                Console.WriteLine("    # install commitlint cli and conventional config".ThemedLowlight(Console.Theme));
                Console.WriteLine("    npm install --save-dev @commitlint/config-conventional @commitlint/cli");
                Console.WriteLine("    # install husky".ThemedLowlight(Console.Theme));
                Console.WriteLine("    npm install husky --save-dev");
                Console.WriteLine("    # activate git hooks".ThemedLowlight(Console.Theme));
                Console.WriteLine("    npx husky install");
                Console.WriteLine("    # add git commit message hook".ThemedLowlight(Console.Theme));
                Console.WriteLine("    npx husky add .husky/commit-msg 'npx --no -- commitlint --edit \"$1\"'");
                Console.WriteLine();
            }

            Console.WriteHeader("(4) Coding standards");
            Console.WriteLine("It is nice to set up code standards and quality rules in a monorepo because they will automatically apply to all new projects.");
            Console.WriteLine("I use StyleCop to force styles of the code. https://bit.ly/3N885PS");
            Console.WriteLine("And Sonar analyzers against code smell and vulnerabilities. https://bit.ly/3iiRL0J");
            Console.WriteLine("By default, the setting is strict; the code styles are set as build errors and most sonar rules as warnings.");
            Console.WriteLine(
                "It can be challenging for the already existing codebase; if you need to tweak any rule's severity, change it in the ",
                ".editorconfig".ThemedHighlight(Console.Theme),
                " file.");

            Console.WriteLine();
            var codingStandards = Console.MultiSelect(new MultiSelectOptions<string>
            {
                Items = new[] { "StyleCop", "Sonar", "Make it less strict (only warnings)" },
                Message = "Select how to set up coding standards",
                DefaultValues = new[] { "StyleCop", "Sonar" },
            }).ToList();

            var useStyleCop = codingStandards.Contains("StyleCop");
            var useSonar = codingStandards.Contains("Sonar");
            var useLessStrictCodingRules = codingStandards.Contains("Make it less strict (only warnings)");

            Console.WriteLine();
            Console.WriteLine("You went through the most important aspects, but there is much more to discuss and consider. Fore more info please visit our detailed documentation about a mono-repository at https://bit.ly/37DZ1SB");

            Console.WriteLine();
            Console.WriteImportant("A new monorepo is about to be created in the current directory.");
            Console.WriteLine();
            Console.WriteHeader("Selected features");

            var featureList = new List<string>();

            if (useCentralDependencies)
            {
                featureList.Add(FeatureNames.Packages);
                Console.WriteLine("  - Central package version management");
            }

            if (useVersioning)
            {
                featureList.Add(FeatureNames.GitVersion);
                Console.WriteLine("  - Git-based versioning system");
            }

            if (useCodeViewSeparation)
            {
                Console.WriteLine("  - Microsoft.Build.Traversal for simple building by tools");
            }

            if (useCommitLint)
            {
                Console.WriteLine("  - CommitLint for conventional commit messages");
            }

            if (useStyleCop)
            {
                Console.WriteLine("  - StyleCop for coding styles");
            }

            if (useSonar)
            {
                Console.WriteLine("  - Sonar analyzers for better code");
            }

            if (useLessStrictCodingRules)
            {
                Console.WriteLine("  - Less strict coding standards");
            }

            Console.WriteLine();
            if (!Console.Confirm("Do you want to proceed and prepare everything"))
            {
                return ExitCodes.WARNING_ABORTED;
            }

            if (_fileSystem.Directory.GetFiles(Environment.CurrentDirectory, "*", SearchOption.TopDirectoryOnly).Length > 0
                && !Console.Confirm("The directory is not empty; some files could be overwritten. Should I continue", false))
            {
                return ExitCodes.WARNING_ABORTED;
            }

            Console.WriteLine();
            Console.WriteHeader("Created files");

            // TODO: [P2] needs to be properly controlled by picked options
            featureList.AddRange(new[]
            {
                FeatureNames.GitVersion,
                FeatureNames.Packages,
                FeatureNames.BuildTraversal,
                FeatureNames.TestsXunit,
                FeatureNames.TestsNunit,
                FeatureNames.Stylecop,
                FeatureNames.Sonar,
            });

            var featureProvider = FeatureProvider.Build(featureList);

            // .editorconfig
            var editorConfig = new DotEditorConfigT4();
            await _fileSystem.File.WriteAllTextAsync(FileNames.DotEditorConfig, editorConfig.TransformText());
            Console.WriteLine($"    {FileNames.DotEditorConfig}");

            // .gitattributes
            var gitAttributes = new DotGitAttributesT4();
            await _fileSystem.File.WriteAllTextAsync(FileNames.DotGitAttributes, gitAttributes.TransformText());
            Console.WriteLine($"    {FileNames.DotGitAttributes}");

            // .gitignore
            var gitIgnore = new DotGitIgnoreT4();
            await _fileSystem.File.WriteAllTextAsync(FileNames.DotGitIgnore, gitIgnore.TransformText());
            Console.WriteLine($"    {FileNames.DotGitIgnore}");

            // .vsconfig
            var vsConfig = new DotVsConfigT4();
            await _fileSystem.File.WriteAllTextAsync(FileNames.DotVsConfig, vsConfig.TransformText());
            Console.WriteLine($"    {FileNames.DotVsConfig}");

            // Directory.Build.props
            var directoryBuildProps = new DirectoryBuildPropsT4(featureProvider);
            await _fileSystem.File.WriteAllTextAsync(FileNames.DirectoryBuildProps, directoryBuildProps.TransformText());
            Console.WriteLine($"    {FileNames.DirectoryBuildProps}");

            // Directory.Packages.props
            if (useCentralDependencies)
            {
                var directoryPackages = new RootDirectoryPackagesPropsT4(featureProvider);
                await _fileSystem.File.WriteAllTextAsync(FileNames.DirectoryPackagesProps, directoryPackages.TransformText());
                Console.WriteLine($"    {FileNames.DirectoryPackagesProps}");
            }

            // glogal.json
            var globalJson = new GlobalJsonT4(featureProvider);
            await _fileSystem.File.WriteAllTextAsync(FileNames.GlogalJson, globalJson.TransformText());
            Console.WriteLine($"    {FileNames.GlogalJson}");

            // mrepo.json
            var mrepoJson = new MrepoJsonT4(new MrepoJsonModel
            {
                Name = _fileSystem.Path.GetFileName(Environment.CurrentDirectory),
                Description = "An awesome .net mono-repository.",
                Features = string.Join(", ", featureList.Select(f => $"\"{f}\"")),
            });
            await _fileSystem.File.WriteAllTextAsync(FileNames.MrepoJson, mrepoJson.TransformText());
            Console.WriteLine($"    {FileNames.MrepoJson}");

            // nuget.config
            var nugetConfig = new NugetConfigT4();
            await _fileSystem.File.WriteAllTextAsync(FileNames.NugetConfig, nugetConfig.TransformText());
            Console.WriteLine($"    {FileNames.NugetConfig}");

            // stylecop.json
            var stylecopJson = new StylecopJsonT4();
            await _fileSystem.File.WriteAllTextAsync(FileNames.StylecopJson, stylecopJson.TransformText());
            Console.WriteLine($"    {FileNames.StylecopJson}");

            // version.json
            if (useVersioning)
            {
                var versionJson = new RootVersionJsonT4();
                await _fileSystem.File.WriteAllTextAsync(FileNames.VersionJson, versionJson.TransformText());
                Console.WriteLine($"    {FileNames.VersionJson}");
            }

            // src/.stylecop/GlobalStylecopSuppressions.cs
            var stylecopGlobalSuppressions = new StylecopGlobalSuppressionsT4();
            _fileSystem.Directory.CreateDirectory("src\\.stylecop");
            await _fileSystem.File.WriteAllTextAsync($"src\\.stylecop\\{FileNames.StylecopGlobalSuppressions}", stylecopGlobalSuppressions.TransformText());
            Console.WriteLine($"    src/.stylecop/{FileNames.StylecopGlobalSuppressions}");

            // src/Directory.Build.Proj
            var directoryBuildProj = new DirectoryBuildProjT4();
            await _fileSystem.File.WriteAllTextAsync($"src\\{FileNames.DirectoryBuildProj}", directoryBuildProj.TransformText());
            Console.WriteLine($"    src/{FileNames.DirectoryBuildProj}");

            Console.WriteLine();
            Console.WriteLine("The last step is to make this folder a Git repository:");
            Console.WriteLine();
            Console.WriteLine("    # initalise a Git repository".ThemedLowlight(Console.Theme));
            Console.WriteLine("    git init -b main");
            Console.WriteLine("    # stage new files for the first commit; to unstage a file, use 'git reset HEAD <FILE-PATH>'".ThemedLowlight(Console.Theme));
            Console.WriteLine("    # (?) to unstage a file, use 'git reset HEAD <FILE-PATH>'".ThemedLowlight(Console.Theme));
            Console.WriteLine("    git add .");
            Console.WriteLine("    # commit staged files and prepares them to be pushed to a remote repository".ThemedLowlight(Console.Theme));
            Console.WriteLine("    # (?) to remove this commit and modify the files, use 'git reset --soft HEAD~1'".ThemedLowlight(Console.Theme));
            Console.WriteLine("    git commit -m \"chore: init the mono-repo\"");
            Console.WriteLine("    # (?) set the new remote; for example GitHub or Azure DevOps".ThemedLowlight(Console.Theme));
            Console.WriteLine("    git remote add origin <REMOTE-URL>");
            Console.WriteLine("    # (?) verify the new remote URL".ThemedLowlight(Console.Theme));
            Console.WriteLine("    git remote -v");
            Console.WriteLine("    # (?) push the changes to the remote repository you specified as the origin".ThemedLowlight(Console.Theme));
            Console.WriteLine("    git push origin main");
            Console.WriteLine();

            return ExitCodes.SUCCESS;
        }
    }
}
