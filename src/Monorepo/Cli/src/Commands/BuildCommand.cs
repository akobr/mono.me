using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Features;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Operations.Strategies;
using _42.Monorepo.Cli.Scripting;
using _42.Monorepo.Cli.Templates;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command(CommandNames.BUILD, "b", Description = "Build a specific location.")]
    public class BuildCommand : BaseCommand
    {
        private readonly IFileSystem _fileSystem;
        private readonly IScriptingService _scripting;
        private readonly IItemFullOptionsProvider _optionsProvider;
        private readonly IFeatureProvider _featureProvider;
        private readonly CommandLineApplication _application;

        public BuildCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context,
            IScriptingService scripting,
            IItemFullOptionsProvider optionsProvider,
            IFeatureProvider featureProvider,
            CommandLineApplication application)
            : base(console, context)
        {
            _fileSystem = fileSystem;
            _scripting = scripting;
            _optionsProvider = optionsProvider;
            _featureProvider = featureProvider;
            _application = application;
        }

        [Option("--path", CommandOptionType.SingleValue, Description = "Relative path in the mono-repository to build.")]
        public string? RelativePath { get; set; } = string.Empty;

        [Option("-l|--profile", CommandOptionType.SingleValue, Description = "The profile of the build. Specify which Directory.<OPERATION>.<PROFILE>.proj file is used or which build script to use.")]
        public string? Profile { get; set; } = string.Empty;

        [Option("-c|--clean", CommandOptionType.NoValue, Description = "Perform clean operation.")]
        public bool ShouldClean { get; set; }

        [Option("-r|--restore", CommandOptionType.NoValue, Description = "Perform restore operation.")]
        public bool ShouldRestore { get; set; }

        [Option("-b|--build", CommandOptionType.NoValue, Description = "Perform build operation (default).")]
        public bool ShouldBuild { get; set; }

        [Option("-t|--test", CommandOptionType.NoValue, Description = "Perform test operation.")]
        public bool ShouldTest { get; set; }

        [Option("-p|--pack", CommandOptionType.NoValue, Description = "Perform pack operation.")]
        public bool ShouldPack { get; set; }

        [Option("-x|--run", CommandOptionType.NoValue, Description = "Run the project or a startup project.")]
        public bool ShouldRun { get; set; }

        protected override async Task<int?> ExecutePreconditionsAsync()
        {
            await base.ExecutePreconditionsAsync();

            if (string.IsNullOrWhiteSpace(RelativePath))
            {
                return null;
            }

            Context.ReInitialize(RelativePath);
            return null;
        }

        protected override async Task<int> ExecuteAsync()
        {
            var isCustom = false;

            if (ShouldClean)
            {
                isCustom = true;
                var code = await ExecuteOperationAsync("clean");

                if (code != ExitCodes.SUCCESS)
                {
                    return code;
                }
            }

            if (ShouldRestore)
            {
                isCustom = true;
                var code = await ExecuteOperationAsync("restore");

                if (code != ExitCodes.SUCCESS)
                {
                    return code;
                }
            }

            if (ShouldBuild)
            {
                isCustom = true;
                var code = await ExecuteOperationAsync("build");

                if (code != ExitCodes.SUCCESS)
                {
                    return code;
                }
            }

            if (ShouldTest)
            {
                isCustom = true;
                var code = await ExecuteOperationAsync("test");

                if (code != ExitCodes.SUCCESS)
                {
                    return code;
                }
            }

            if (ShouldPack)
            {
                isCustom = true;
                var code = await ExecuteOperationAsync("pack");

                if (code != ExitCodes.SUCCESS)
                {
                    return code;
                }
            }

            if (ShouldRun)
            {
                var code = await RunAnyItemAsync();
                return code;
            }

            if (isCustom)
            {
                return ExitCodes.SUCCESS;
            }

            return await ExecuteOperationAsync("build");
        }

        private async Task<int> ExecuteOperationAsync(string operation)
        {
            var fullPath = Context.Item.Record.Path;

            if (Context.Item.Record.Type is RecordType.Repository)
            {
                fullPath = _fileSystem.Path.Combine(fullPath, Constants.SOURCE_DIRECTORY_NAME);
            }

            var profile = !string.IsNullOrWhiteSpace(Profile) ? Profile.Trim() : string.Empty;
            var hasProfile = !string.IsNullOrWhiteSpace(profile);
            var useTraversal = _featureProvider.IsEnabled(FeatureNames.BuildTraversal);

            var scriptName = hasProfile ? $"{operation}-{profile}" : operation;
            var scriptContext = new ScriptContext(scriptName, Context.Item) { Args = _application.RemainingArguments };

            // try to run the script from explicit script definition (script tree)
            if (_scripting.HasScript(scriptContext))
            {
                var scriptOutput = await _scripting.ExecuteScriptAsync(scriptContext);
                return scriptOutput;
            }

            // try to run the build operation from traversal target file
            if (useTraversal)
            {
                var traversalOutput = await TryOperateWithTraversal(Context.Item, operation, fullPath, profile);

                if (traversalOutput.HasValue)
                {
                    return traversalOutput.Value;
                }
            }

            // try fallback to default item type (implicit type)
            var fallbackOutput = await TryFallbackToDefaultItemType(Context.Item, scriptName);

            if (fallbackOutput.HasValue)
            {
                return fallbackOutput.Value;
            }

            // if there is no script and traversal target file is missing ask to create it
            if (useTraversal)
            {
                var operationNormalized = char.ToUpperInvariant(operation[0]) + operation[1..].ToLowerInvariant();
                var basicTraversalTarget = "Directory.Build.proj";
                var isNotBuild = operation != "build";
                Console.WriteImportant("No custom script or traversal target file found.");
                Console.WriteLine($" > {scriptName}");

                if (hasProfile)
                {
                    Console.WriteLine($" > Directory.{operationNormalized}.{profile}.proj");

                    if (isNotBuild)
                    {
                        basicTraversalTarget = $"Directory.Build.{profile}.proj";
                        Console.WriteLine($" > Directory.Build.{profile}.proj");
                    }
                }
                else
                {
                    Console.WriteLine($" > Directory.{operationNormalized}.proj");

                    if (isNotBuild)
                    {
                        Console.WriteLine($" > Directory.Build.proj");
                    }
                }

                Console.WriteLine();
                var createDirectoryBuildFile = Console.Confirm($"Create new traversal target {basicTraversalTarget}");

                if (createDirectoryBuildFile)
                {
                    var template = new DirectoryBuildProjT4();
                    var filePath = _fileSystem.Path.Combine(fullPath, basicTraversalTarget);
                    await _fileSystem.File.WriteAllTextAsync(filePath, template.TransformText());
                    Console.WriteLine(
                        "The traversal build file has been created, please run ".ThemedLowlight(Console.Theme),
                        "mrepo build",
                        " again.".ThemedLowlight(Console.Theme));
                    return ExitCodes.SUCCESS;
                }
            }
            else
            {
                Console.WriteImportant($"No custom script '{scriptName}' found.");
            }

            return ExitCodes.ERROR_WRONG_PLACE;
        }

        private async Task<int> RunAnyItemAsync()
        {
            var item = Context.Item;
            var startupProjectRepoPath = Context.Item.Record.RepoRelativePath;

            if (item.Record.Type is RecordType.Special)
            {
                return await RunItemAsync(item);
            }

            if (item.Record.Type != RecordType.Project)
            {
                var options = _optionsProvider.GetWorksteadOptions(Context.Item.Record.RepoRelativePath);
                startupProjectRepoPath = options.GetStartupProject();

                if (string.IsNullOrWhiteSpace(startupProjectRepoPath))
                {
                    Console.WriteImportant($"No startup project for: {item.Record.RepoRelativePath}, set some in mrepo.json");
                    return ExitCodes.ERROR_WRONG_PLACE;
                }

                var projectRecord = MonorepoDirectoryFunctions.GetRecord(_fileSystem.Path.Combine(Context.Repository.Record.Path, startupProjectRepoPath));
                item = Context.Repository.TryGetDescendant(projectRecord);
            }

            if (item is not IProject)
            {
                Console.WriteImportant($"Unknown startup project: {startupProjectRepoPath}");
                return ExitCodes.ERROR_WRONG_PLACE;
            }

            return await RunItemAsync(item);
        }

        private async Task<int> RunItemAsync(IItem item)
        {
            var profile = !string.IsNullOrWhiteSpace(Profile) ? Profile.Trim() : string.Empty;
            var hasProfile = !string.IsNullOrEmpty(profile);

            var scriptName = hasProfile ? $"run-{profile}" : "run";
            var scriptContext = new ScriptContext(scriptName, item) { Args = _application.RemainingArguments };

            // try to run the script from explicit script definition (script tree)
            if (_scripting.HasScript(scriptContext))
            {
                return await _scripting.ExecuteScriptAsync(scriptContext);
            }

            // try fallback to default item type (implicit type)
            var fallbackOutput = await TryFallbackToDefaultItemType(item, scriptName);

            if (fallbackOutput.HasValue)
            {
                return fallbackOutput.Value;
            }

            // if there is no script and no fallback in default item type, try to run the .net project
            if (item is IProject projectItem)
            {
                var projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item, _fileSystem);

                if (!_fileSystem.File.Exists(projectFilePath))
                {
                    Console.WriteImportant("Project file not found.");
                    return ExitCodes.ERROR_WRONG_PLACE;
                }

                var script = $"dotnet run --project {projectFilePath}";
                var context = new ScriptContext(item, script: script, args: _application.RemainingArguments);
                var scriptOutput = await _scripting.ExecuteScriptAsync(context);
                return scriptOutput;
            }

            Console.WriteImportant("Runnable target not found.");
            return ExitCodes.ERROR_WRONG_PLACE;
        }

        private async Task<int?> TryOperateWithTraversal(IItem item, string operation, string fullPath, string profile)
        {
            var buildTarget = GetTraversalTargetFile(operation, fullPath, profile);

            if (!_fileSystem.File.Exists(_fileSystem.Path.Combine(fullPath, buildTarget)))
            {
                return null;
            }

            var script = $"dotnet {operation} {buildTarget}";
            var context = new ScriptContext(item, script: script, args: _application.RemainingArguments);
            var scriptOutput = await _scripting.ExecuteScriptAsync(context);
            return scriptOutput;
        }

        private string GetTraversalTargetFile(string operation, string fullPath, string profile)
        {
            var buildTarget = "Directory.Build.proj";

            if (string.IsNullOrEmpty(profile))
            {
                buildTarget = $"Directory.{char.ToUpperInvariant(operation[0])}{operation[1..].ToLowerInvariant()}.proj";

                if (!_fileSystem.File.Exists(_fileSystem.Path.Combine(fullPath, buildTarget)))
                {
                    buildTarget = "Directory.Build.proj";
                }
            }
            else
            {
                buildTarget = $"Directory.{char.ToUpperInvariant(operation[0])}{operation[1..].ToLowerInvariant()}.{profile}.proj";

                if (!_fileSystem.File.Exists(_fileSystem.Path.Combine(fullPath, buildTarget)))
                {
                    buildTarget = $"Directory.Build.{profile}.proj";
                }
            }

            return buildTarget;
        }

        private async Task<int?> TryFallbackToDefaultItemType(IItem item, string scriptName)
        {
            var typedItemOptions = _optionsProvider.GetItemOptions(item.Record);

            if (!typedItemOptions.Scripts.TryGetValue(scriptName, out var script))
            {
                return null;
            }

            // TODO: [P2] this needs to be called through the scripting service (when types are properly included in it)
            var context = new ScriptContext(item, script: script, args: _application.RemainingArguments);
            var scriptOutput = await _scripting.ExecuteScriptAsync(context);
            return scriptOutput;
        }
    }
}
