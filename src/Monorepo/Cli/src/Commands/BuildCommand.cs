using System.IO;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Features;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Operations.Strategies;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Scripting;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command(CommandNames.BUILD, Description = "Build a specific location.")]
    public class BuildCommand : BaseCommand
    {
        private readonly IScriptingService _scripting;
        private readonly IItemFullOptionsProvider _optionsProvider;
        private readonly IFeatureProvider _featureProvider;
        private readonly CommandLineApplication _application;

        public BuildCommand(
            IExtendedConsole console,
            ICommandContext context,
            IScriptingService scripting,
            IItemFullOptionsProvider optionsProvider,
            IFeatureProvider featureProvider,
            CommandLineApplication application)
            : base(console, context)
        {
            _scripting = scripting;
            _optionsProvider = optionsProvider;
            _featureProvider = featureProvider;
            _application = application;
        }

        [Option("--path", CommandOptionType.SingleValue, Description = "Relative path in the mono-repository to build.")]
        public string? RelativePath { get; set; } = string.Empty;

        [Option("-l|--profile", CommandOptionType.SingleValue, Description = "The profile of the build. Specify which Directory.<PROFILE>.proj file is used or which build script to use.")]
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

        protected override async Task ExecutePreconditionsAsync()
        {
            await base.ExecutePreconditionsAsync();

            if (string.IsNullOrWhiteSpace(RelativePath))
            {
                return;
            }

            Context.ReInitialize(RelativePath);
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

            if (ShouldPack)
            {
                isCustom = true;
                var code = await ExecuteOperationAsync("pack");

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

            if (ShouldRun)
            {
                var code = await RunProjectAsync();
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
            var profile = !string.IsNullOrWhiteSpace(Profile) ? Profile.Trim() : string.Empty;
            var hasProfile = !string.IsNullOrEmpty(profile);
            var useTraversal = _featureProvider.IsEnabled(FeatureNames.BuildTraversal);

            var scriptName = hasProfile ? $"{operation}-{profile}" : operation;
            var scriptContext = new ScriptContext(scriptName, Context.Item) { Args = _application.RemainingArguments };

            if (_scripting.HasScript(scriptContext))
            {
                var scriptOutput = await _scripting.ExecuteScriptAsync(scriptContext);
                return scriptOutput;
            }

            if (useTraversal)
            {
                var traversalOutput = await TryOperateWithTraversal(operation, fullPath, profile);

                if (traversalOutput.HasValue)
                {
                    return traversalOutput.Value;
                }
            }

            var fallbackOutput = await TryFallbackToDefaultProject(scriptName, fullPath);

            if (fallbackOutput.HasValue)
            {
                return fallbackOutput.Value;
            }

            if (useTraversal)
            {
                Console.WriteImportant($"No custom script or traversal target file found.");
                Console.WriteLine($" > {scriptName}");
                Console.WriteLine($" > Directory.Build.proj");
            }
            else
            {
                Console.WriteImportant($"No custom script '{scriptName}' found.");
            }

            return ExitCodes.ERROR_WRONG_PLACE;
        }

        private async Task<int> RunProjectAsync()
        {
            var projectItem = Context.Item;
            var startupProjectRepoPath = Context.Item.Record.RepoRelativePath;

            if (projectItem.Record.Type != Model.RecordType.Project)
            {
                var options = _optionsProvider.GetWorksteadOptions(Context.Item.Record.RepoRelativePath);
                startupProjectRepoPath = options.GetStartupProject();

                if (string.IsNullOrWhiteSpace(startupProjectRepoPath))
                {
                    Console.WriteImportant($"No startup project for: {projectItem.Record.RepoRelativePath}");
                }

                var projectRecord = MonorepoDirectoryFunctions.GetRecord(Path.Combine(Context.Repository.Record.Path, startupProjectRepoPath));
                projectItem = Context.Repository.TryGetDescendant(projectRecord);
            }

            if (projectItem is not IProject targetProject)
            {
                Console.WriteImportant($"Unknown startup project: {startupProjectRepoPath}");
                return ExitCodes.ERROR_WRONG_PLACE;
            }

            string projectFilePath = ProjectStrategyHelper.GetProjectFilePath(targetProject);
            string script = $"dotnet run --project {projectFilePath}";
            var scriptOutput = await _scripting.ExecuteScriptAsync(script, Context.Item.Record.Path);
            return scriptOutput;
        }

        private async Task<int?> TryOperateWithTraversal(string operation, string fullPath, string profile)
        {
            var buildTarget = GetTraversalTargetFile(operation, fullPath, profile);

            if (!File.Exists(Path.Combine(fullPath, buildTarget)))
            {
                return null;
            }

            var script = $"dotnet {operation} {buildTarget} {string.Join(' ', _application.RemainingArguments)}";
            var scriptOutput = await _scripting.ExecuteScriptAsync(script, fullPath);
            return scriptOutput;
        }

        private static string GetTraversalTargetFile(string operation, string fullPath, string profile)
        {
            var buildTarget = "Directory.Build.proj";

            if (string.IsNullOrEmpty(profile))
            {
                buildTarget = $"Directory.{char.ToUpperInvariant(operation[0])}{operation[1..].ToLowerInvariant()}.proj";

                if (!File.Exists(Path.Combine(fullPath, buildTarget)))
                {
                    buildTarget = "Directory.Build.proj";
                }
            }
            else
            {
                buildTarget = $"Directory.{char.ToUpperInvariant(operation[0])}{operation[1..].ToLowerInvariant()}.{profile}.proj";

                if (!File.Exists(Path.Combine(fullPath, buildTarget)))
                {
                    buildTarget = $"Directory.Build.{profile}.proj";
                }
            }

            return buildTarget;
        }

        private async Task<int?> TryFallbackToDefaultProject(string scriptName, string fullPath)
        {
            var typedItemOptions = _optionsProvider.GetItemOptions(Context.Item.Record);

            if (!typedItemOptions.Scripts.TryGetValue(scriptName, out var script))
            {
                return null;
            }

            var scriptOutput = await _scripting.ExecuteScriptAsync(script, fullPath);
            return scriptOutput;
        }
    }
}
