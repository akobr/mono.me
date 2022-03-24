using System.IO;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Features;
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

        [Option("-n|--clean", CommandOptionType.NoValue, Description = "Perform clean operation.")]
        public bool ShouldClean { get; set; }

        [Option("-r|--restore", CommandOptionType.NoValue, Description = "Perform restore operation.")]
        public bool ShouldRestore { get; set; }

        [Option("-b|--build", CommandOptionType.NoValue, Description = "Perform build operation (default).")]
        public bool ShouldBuild { get; set; }

        [Option("-t|--test", CommandOptionType.NoValue, Description = "Perform test operation.")]
        public bool ShouldTest { get; set; }

        [Option("-p|--pack", CommandOptionType.NoValue, Description = "Perform pack operation.")]
        public bool ShouldPack { get; set; }

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

            if (isCustom)
            {
                return ExitCodes.SUCCESS;
            }

            return await ExecuteOperationAsync("build");
        }

        private Task<int> ExecuteOperationAsync(string operation)
        {
            var fullPath = Context.Item.Record.Path;
            var profile = !string.IsNullOrWhiteSpace(Profile) ? Profile.Trim() : string.Empty;
            var hasProfile = !string.IsNullOrEmpty(profile);

            var scriptName = hasProfile ? $"{operation}-{profile}" : operation;
            var scriptContext = new ScriptContext(scriptName, Context.Item) { Args = _application.RemainingArguments };

            if (_scripting.HasScript(scriptContext))
            {
                return _scripting.ExecuteScriptAsync(scriptContext);
            }

            var typedItemOptions = _optionsProvider.GetItemOptions(Context.Item.Record);

            if (typedItemOptions.Scripts.TryGetValue(scriptName, out var script))
            {
                return _scripting.ExecuteScriptAsync(script);
            }

            if (!_featureProvider.IsEnabled(FeatureNames.Traversal))
            {
                Console.WriteImportant($"No custom script '{scriptName}' has been found.");
                return Task.FromResult(ExitCodes.ERROR_WRONG_PLACE);
            }

            // TODO: [P2] Has a default for dotnet project
            return OperateWithTraversal(operation, fullPath, profile, hasProfile);
        }

        private Task<int> OperateWithTraversal(string operation, string fullPath, string profile, bool hasProfile)
        {
            var buildTarget = "Directory.Build.proj";

            if (hasProfile)
            {
                buildTarget = $"Directory.{char.ToUpperInvariant(operation[0])}{operation[1..].ToLowerInvariant()}.{profile}.proj";

                if (!File.Exists(Path.Combine(fullPath, buildTarget)))
                {
                    buildTarget = $"Directory.Build.{profile}.proj";
                }
            }
            else
            {
                buildTarget = $"Directory.{char.ToUpperInvariant(operation[0])}{operation[1..].ToLowerInvariant()}.proj";

                if (!File.Exists(Path.Combine(fullPath, buildTarget)))
                {
                    buildTarget = "Directory.Build.proj";
                }
            }

            if (!File.Exists(buildTarget))
            {
                Console.WriteImportant($"No custom script or '{buildTarget}' target file found.");
                return Task.FromResult(ExitCodes.ERROR_WRONG_PLACE);
            }

            var script = $"dotnet {operation} {buildTarget} {string.Join(' ', _application.RemainingArguments)}";
            return _scripting.ExecuteScriptAsync(script);
        }
    }
}
