using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Model.Records;
using Microsoft.Extensions.Options;

namespace _42.Monorepo.Cli.Scripting
{
    public class ScriptingService : IScriptingService
    {
        private readonly MonoRepoOptions repositoryOptions;
        private readonly IItemFullOptionsProvider itemOptionsProvider;
        private readonly ScriptTree scriptTree;

        public ScriptingService(
            IOptions<MonoRepoOptions> repositoryOptions,
            IItemFullOptionsProvider itemOptionsProvider)
        {
            this.repositoryOptions = repositoryOptions.Value;
            this.itemOptionsProvider = itemOptionsProvider;
            scriptTree = InitialiseScriptTree();
        }

        public bool HasScript(IScriptContext context)
        {
            return scriptTree.HasScript(context.Item.Record.RepoRelativePath, context.ScriptName);
        }

        public IEnumerable<string> GetAvailableScriptNames(IItem item)
        {
            return scriptTree.GetAvailableScriptNames(item.Record.RepoRelativePath);
        }

        public Task<int> ExecuteScriptAsync(IScriptContext context, CancellationToken cancellationToken = default)
        {
            var record = context.Item.Record;
            var script = scriptTree.GetScript(record.RepoRelativePath, context.ScriptName);

            return string.IsNullOrEmpty(script)
                ? Task.FromResult(ExitCodes.ERROR_WRONG_INPUT)
                : ExecuteScriptAsync(script, record.Path, cancellationToken);
        }

        public async Task<int> ExecuteScriptAsync(string script, string? workingDirectory = null, CancellationToken cancellationToken = default)
        {
            var arguments = script.Replace("\"", "\"\"\"");

            ProcessStartInfo startInfo = new(repositoryOptions.Shell ?? "powershell", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };

            Console.WriteLine($"{startInfo.WorkingDirectory}> {script}"); // TODO [P2]
            var process = Process.Start(startInfo);

            if (process is null
                || process.HasExited)
            {
                return ExitCodes.WARNING_ABORTED;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }

        // TODO: [P2] Prepare script with arguments (parameter replacement)
        private string PrepareScriptToExecute(string script, IScriptContext context)
        {
            return string.Empty;
        }

        private ScriptTree InitialiseScriptTree()
        {
            var repository = MonorepoDirectoryFunctions.GetMonoRepository();

            if (!repository.IsValid)
            {
                return ScriptTree.Empty;
            }

            var rootNode = new ScriptTreeNode(repository.Name, null, repositoryOptions.Scripts);
            var tree = new ScriptTree(rootNode);
            InitialiseScriptFiles(rootNode.ScriptMap, repository.Path);

            foreach (var itemOptions in itemOptionsProvider.GetAllOptions())
            {
                tree.GetOrCreateTargetNode(itemOptions.Path);
            }

            return tree;
        }

        private static void InitialiseScriptFiles(Dictionary<string, string> target, string repositoryPath)
        {
            var scriptsFolder = Path.Combine(repositoryPath, Constants.SCRIPTS_DIRECTORY_REPO_PATH);

            if (!Directory.Exists(scriptsFolder))
            {
                return;
            }

            foreach (var scriptFilePath in Directory.GetFiles(scriptsFolder, "*.ps1", SearchOption.TopDirectoryOnly))
            {
                var scriptName = Path.GetFileNameWithoutExtension(scriptFilePath);
                var script = $"Set-ExecutionPolicy Bypass -Scope Process -Force; iex {scriptFilePath}";
                target.TryAdd(scriptName, script);
            }
        }
    }
}
