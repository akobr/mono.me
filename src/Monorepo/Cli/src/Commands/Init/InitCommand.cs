using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Features;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Templates;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

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

        protected override async Task<int> ExecuteAsync()
        {
#if !DEBUG
            if (Context.IsValid)
            {
                Console.WriteImportant("You are already in a ", "mono-repository".ThemedHighlight(Console.Theme), ", hurray!");
                return Task.FromResult(ExitCodes.WARNING_NO_WORK_NEEDED);
            }
#endif

            if (!Console.Confirm("Do you want to create a new .net mono-repository in current folder"))
            {
                return ExitCodes.WARNING_ABORTED;
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
                " even when it is still in preview, because this is the out-of-the-box solution in .net SDK which can be easily changed to any other strategy.");


            SelectOptions<Feature> featureOptions = new()
            {
                Items = new List<Feature>
                {
                    new("CentralPackageVersionManagement",
                        "Central package version management [Preview]",
                        "Recommended. A baked in solution into .NET Core SDK (from 3.1.300), out-of-the-box solution with Directory.Packages.props file, but still a preview feature. https://bit.ly/3oKJCpq"),
                    new("CentralPackageVersionsSdk",
                        "External MsBuild SDK",
                        "A custom MsBuild SDK built by NuGet team, named as Microsoft.Build.CentralPackageVersions. https://bit.ly/3GMloRG"),
                    new("DirectoryBuildTargets",
                        "Directory.Build.props [MsBuild 15+]",
                        "Use of hierarchical Directory.Build.props and posibility to update version of package reference by MsBuild 15 and newer."),
                    new("Paket",
                        "Paket package manager",
                        "An alternative package manager to NuGet for .NET projects, which has some great features. Currently not yet supported by my tooling. https://bit.ly/3oHTJLp"),
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

            var useCentralDependencies = Console.Confirm("Should I turn on the Central package version management");
            Console.WriteLine();

            if (useCentralDependencies)
            {
                Console.WriteLine("Detailed information about central package version management:");
                Console.WriteLine("    http://GitHub.todo.pages");
            }
            else
            {
                Console.WriteLine("All dependencies must be managed independenty inside each project file. The advantages of centralized approach are described at:");
                Console.WriteLine("    http://GitHub.todo.pages");
            }

            Console.WriteLine();
            Console.WriteHeader("(2) Versioning");
            Console.WriteLine("If you are lucky and all projects are used only internally inside your mono-repository you don't need to care about versioning and everything will be just a project reference.");
            Console.WriteLine("More scary case is when you need to serve multiple libraries as NuGet packages or through any other packing system, then you should manage multiple versions and independent releases.");
            Console.WriteLine("Don't be scared too much even for this approach I put together a couple of tooling and recommendations as part of this toolset.");
            Console.WriteLine();
            Console.WriteLine(
                "Mine versioning system is using git history as the only source of the truth and a version. It is based on ",
                "NerdBank.GetVersioning".ThemedHighlight(Console.Theme),
                " library which has been slightly updated to a mono-repository needs. https://bit.ly/3sAjIpy");
            Console.WriteLine(
                "Principle is simple, the monorepo contains one or multiple ",
                "version.json".ThemedLowlight(Console.Theme),
                " files which define and control version(s) for projects inside the repository where every single commit can be built and produce a unique version.");

            Console.WriteLine();
            var useVersioning = Console.Confirm("Configure and use mentioned versioning system");
            Console.WriteLine();

            if (useVersioning)
            {
                Console.WriteLine("Detailed information about the versioning:");
                Console.WriteLine("    http://GitHub.todo.pages");
            }
            else
            {
                Console.WriteLine("Guide how to manualy configure versions in .net project:");
                Console.WriteLine("    http://GitHub.todo.pages");
            }

            Console.WriteLine();
            Console.WriteHeader("(3) Build and CI tooling");
            Console.WriteLine("First recomendation is to separate human and machine view of the codebase. The solution file(s) should be used only by developers and never by any machine or a bot.");
            Console.WriteLine(
                "The ",
                "Microsoft.Build.Traversal".ThemedHighlight(Console.Theme),
                " msbuild SDK is used to have the second view, for a machine. The magic is done by ",
                "Directory.Build.proj".ThemedLowlight(Console.Theme),
                " files, which allow to have custom build per any point/directory of the mono-repository. https://bit.ly/3sJl7tQ");
            Console.WriteLine("By default my CLI tooling use this system to build through it, if you pick to don't use it you have to write your own scripts for building.");

            Console.WriteLine();
            var useCodeViewSeparation = Console.Confirm("Turn it on and automatically create Directory.Build.proj file with each workstead");
            Console.WriteLine();

            if (useCodeViewSeparation)
            {
                Console.WriteLine("My idea how to built from the mono-repository is described here:");
                Console.WriteLine("    http://GitHub.todo.pages");
                Console.WriteLine();
            }

            Console.WriteLine("To simplify releases and their notes I recommend you to start with conventional commits. https://bit.ly/3JsPtHY");
            Console.WriteLine("To push it even one step further, you should setup CommitLint to force any developer to have clean and nice git history ready for automated releases. https://bit.ly/3rLCAm7");
            Console.WriteLine(
                "With all above prerequsities and mine CLI tools for releasing (",
                "mrepo release".ThemedLowlight(Console.Theme),
                "), the release and creation of release notes will be just a piece of cake.");

            Console.WriteLine();
            var useCommitLint = Console.Confirm("Do you want to turn on CommitLint for conventional messages");
            Console.WriteLine();

            if (useCommitLint)
            {
                Console.WriteLine("I will help you with configuration of the CommitLint but the installation needs to be done manually.");
                Console.WriteLine("You need to setup node.js with npm, CommitLint and Husky to force the rules on each commit:");
                Console.WriteLine();
                Console.WriteLine("    # install node.js by Chocolatey".ThemedLowlight(Console.Theme));
                Console.WriteLine("    choco install nodejs-lts");
                Console.WriteLine("    # install comitlint cli and conventional config".ThemedLowlight(Console.Theme));
                Console.WriteLine("    npm install --save-dev @commitlint/config-conventional @commitlint/cli");
                Console.WriteLine("    # install husky".ThemedLowlight(Console.Theme));
                Console.WriteLine("    npm install husky --save-dev");
                Console.WriteLine("    # activate git hooks".ThemedLowlight(Console.Theme));
                Console.WriteLine("    npx husky install");
                Console.WriteLine("    # add git commit message hook".ThemedLowlight(Console.Theme));
                Console.WriteLine("    npx husky add .husky/commit-msg 'npx --no -- commitlint --edit \"$1\"'");
                Console.WriteLine();
            }

            Console.WriteLine("You went through the most important aspects, but there is much more to discuss and consider. Fore more info please visit our detailed documentation about a mono-repository at http://GitHub.todo.pages");

            Console.WriteLine();
            Console.WriteImportant("New monorepo is about to be created in the current directory.");
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
                Console.WriteLine("  - Git based versioning system");
            }

            if (useCodeViewSeparation)
            {
                Console.WriteLine("  - Microsoft.Build.Traversal for simple building by tools");
            }

            if (useCommitLint)
            {
                Console.WriteLine("  - CommitLint for conventional commit messages");
            }

            Console.WriteLine();
            if (!Console.Confirm("Do you want to proceed and prepare everything"))
            {
                return ExitCodes.WARNING_ABORTED;
            }

            if (Directory.GetFiles(Environment.CurrentDirectory, "*", SearchOption.TopDirectoryOnly).Length > 0
                && !Console.Confirm("The directory is not empty some files can be overwritten, should I continue"))
            {
                return ExitCodes.WARNING_ABORTED;
            }

            Console.WriteLine();
            Console.WriteHeader("Created files");

            featureList.AddRange(new[]
            {
                FeatureNames.TestsXunit,
                FeatureNames.TestsNunit,
                FeatureNames.Stylecop,
            });

            var featureProvider = FeatureProvider.Build(featureList);

            // .editorconfig
            var editorConfig = new DotEditorConfigT4();
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.DotEditorConfig, editorConfig.TransformText());
#endif
            Console.WriteLine($"  {FileNames.DotEditorConfig}");

            // .gitattributes
            var gitAttributes = new DotGitAttributesT4();
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.DotGitAttributes, gitAttributes.TransformText());
#endif
            Console.WriteLine($"  {FileNames.DotGitAttributes}");

            // .gitignore
            var gitIgnore = new DotGitIgnoreT4();
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.DotGitIgnore, gitIgnore.TransformText());
#endif
            Console.WriteLine($"  {FileNames.DotGitIgnore}");

            // .vsconfig
            var vsConfig = new DotVsConfigT4();
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.DotVsConfig, vsConfig.TransformText());
#endif
            Console.WriteLine($"  {FileNames.DotVsConfig}");

            // Directory.Build.props
            var directoryBuildProps = new DirectoryBuildPropsT4(featureProvider);
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.DirectoryBuildProps, directoryBuildProps.TransformText());
#endif
            Console.WriteLine($"  {FileNames.DirectoryBuildProps}");

            // Directory.Packages.props
            if (useCentralDependencies)
            {
                var directoryPackages = new RootDirectoryPackagesPropsT4(featureProvider);
#if !DEBUG || TESTING
                await File.WriteAllTextAsync(FileNames.DirectoryPackagesProps, directoryPackages.TransformText());
#endif
                Console.WriteLine($"  {FileNames.DirectoryPackagesProps}");
            }

            // glogal.json
            var globalJson = new GlobalJsonT4(featureProvider);
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.GlogalJson, globalJson.TransformText());
#endif
            Console.WriteLine($"  {FileNames.GlogalJson}");

            // mrepo.json
            var mrepoJson = new MrepoJsonT4(new MrepoJsonModel()
            {
                Name = Path.GetFileName(Environment.CurrentDirectory),
                Description = "An awesome .net mono-repository.",
                Features = string.Join(", ", featureList.Select(f => $"\"{f}\"")),
            });
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.MrepoJson, mrepoJson.TransformText());
#endif
            Console.WriteLine($"  {FileNames.MrepoJson}");

            // nuget.config
            var nugetConfig = new NugetConfigT4();
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.NugetConfig, nugetConfig.TransformText());
#endif
            Console.WriteLine($"  {FileNames.NugetConfig}");

            // stylecop.json
            var stylecopJson = new NugetConfigT4();
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(FileNames.StylecopJson, stylecopJson.TransformText());
#endif
            Console.WriteLine($"  {FileNames.StylecopJson}");

            // version.json
            if (useVersioning)
            {
                var versionJson = new RootVersionJsonT4();
#if !DEBUG || TESTING
                await File.WriteAllTextAsync(FileNames.VersionJson, versionJson.TransformText());
#endif
                Console.WriteLine($"  {FileNames.VersionJson}");
            }

            // src/.stylecop/GlobalStylecopSuppressions.cs
            var stylecopGlobalSuppressions = new StylecopGlobalSuppressionsT4();
#if !DEBUG || TESTING
            Directory.CreateDirectory("src\\.stylecop");
            await File.WriteAllTextAsync($"src\\.stylecop\\{FileNames.StylecopGlobalSuppressions}", stylecopGlobalSuppressions.TransformText());
#endif
            Console.WriteLine($"  src/.stylecop/{FileNames.StylecopGlobalSuppressions}");

            Console.WriteLine();
            Console.WriteLine("Last step is to make this folder a git repository.".ThemedLowlight(Console.Theme));

            return ExitCodes.SUCCESS;
        }
    }
}
