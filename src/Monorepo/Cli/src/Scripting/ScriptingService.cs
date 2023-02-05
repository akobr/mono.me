using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Model.Items;
using Microsoft.Extensions.Options;

namespace _42.Monorepo.Cli.Scripting
{
    public class ScriptingService : IScriptingService
    {
        private readonly MonoRepoOptions _repositoryOptions;
        private readonly IFileSystem _fileSystem;
        private readonly IItemFullOptionsProvider _itemOptionsProvider;
        private readonly ScriptTree _scriptTree;

        public ScriptingService(
            IFileSystem fileSystem,
            IOptions<MonoRepoOptions> repositoryOptions,
            IItemFullOptionsProvider itemOptionsProvider)
        {
            _repositoryOptions = repositoryOptions.Value;
            _fileSystem = fileSystem;
            _itemOptionsProvider = itemOptionsProvider;
            _scriptTree = InitialiseScriptTree();
        }

        public bool HasScript(IScriptContext context)
        {
            return _scriptTree.HasScript(context.Item.Record.RepoRelativePath, context.ScriptName);
        }

        public IEnumerable<string> GetAvailableScriptNames(IItem item)
        {
            return _scriptTree.GetAvailableScriptNames(item.Record.RepoRelativePath);
        }

        public Task<int> ExecuteScriptAsync(IScriptContext context, CancellationToken cancellationToken = default)
        {
            var record = context.Item.Record;
            var script = _scriptTree.GetScript(record.RepoRelativePath, context.ScriptName);

            return string.IsNullOrEmpty(script)
                ? Task.FromResult(ExitCodes.ERROR_WRONG_INPUT)
                : ExecuteScriptAsync(script, record.Path, cancellationToken);
        }

        public async Task<int> ExecuteScriptAsync(string script, string? workingDirectory = null, CancellationToken cancellationToken = default)
        {
            var arguments = script.Replace("\"", "\"\"\"");

            ProcessStartInfo startInfo = new(_repositoryOptions.Shell ?? "powershell", arguments)
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

            var rootNode = new ScriptTreeNode(repository.Name, null, _repositoryOptions.Scripts);
            var tree = new ScriptTree(rootNode);
            InitialiseScriptFiles(rootNode.ScriptMap, repository.Path);

            foreach (var itemOptions in _itemOptionsProvider.GetAllOptions())
            {
                tree.GetOrCreateTargetNode(itemOptions.Path);
            }

            return tree;
        }

        private void InitialiseScriptFiles(Dictionary<string, string> target, string repositoryPath)
        {
            var scriptsFolder = _fileSystem.Path.Combine(repositoryPath, Constants.SCRIPTS_DIRECTORY_REPO_PATH);

            if (!_fileSystem.Directory.Exists(scriptsFolder))
            {
                return;
            }

            foreach (var scriptFilePath in _fileSystem.Directory.GetFiles(scriptsFolder, "*.ps1", SearchOption.TopDirectoryOnly))
            {
                var scriptName = _fileSystem.Path.GetFileNameWithoutExtension(scriptFilePath);
                var script = $"Set-ExecutionPolicy Bypass -Scope Process -Force; iex {scriptFilePath}";
                target.TryAdd(scriptName, script);
            }
        }
    }
}
