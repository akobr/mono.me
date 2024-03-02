using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Operations.Strategies;
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
            if (string.IsNullOrWhiteSpace(context.ScriptName))
            {
                return !string.IsNullOrWhiteSpace(context.Script);
            }

            return _scriptTree.HasScript(context.Item.Record.RepoRelativePath, context.ScriptName);
        }

        public IEnumerable<string> GetAvailableScriptNames(IItem item)
        {
            return _scriptTree.GetAvailableScriptNames(item.Record.RepoRelativePath);
        }

        public Task<int> ExecuteScriptAsync(IScriptContext context, CancellationToken cancellationToken = default)
        {
            var item = context.Item;
            var record = item.Record;
            var script = string.IsNullOrWhiteSpace(context.ScriptName)
                ? context.Script
                : _scriptTree.GetScript(record.RepoRelativePath, context.ScriptName);

            if (string.IsNullOrWhiteSpace(script))
            {
                return Task.FromResult(ExitCodes.ERROR_WRONG_INPUT);
            }

            script = PrepareScriptToExecute(script, context);
            return ExecuteScriptAsync(script, record.Path, SetEnvironment, cancellationToken);

            async Task SetEnvironment(IDictionary<string, string?> variables)
            {
                // paths
                var repoPath = MonorepoDirectoryFunctions.GetMonorepoRootDirectory();
                variables["MREPO_PATH_REPO"] = repoPath;
                variables["MREPO_PATH_ARTIFACTS"] = _fileSystem.Path.Combine(repoPath, ".artifacts");
                variables["MREPO_PATH"] = record.Path.NormalizePath();
                variables["MREPO_PATH_RELATIVE"] = item.Record.RepoRelativePath.NormalizePath();

                // versions
                var versions = await item.GetExactVersionsAsync(cancellationToken);
                variables["MREPO_VERSION"] = versions.SemVersion.ToString();
                variables["MREPO_VERSION_PACKAGE"] = versions.PackageVersion.ToString();
                variables["MREPO_VERSION_ASSEMBLY"] = versions.AssemblyVersion.ToString();
                variables["MREPO_VERSION_INFO"] = versions.AssemblyInformationalVersion;

                // item
                variables["MREPO_TYPE"] = Enum.GetName(item.Record.Type);
                variables["MREPO_NAME"] = item.Record.Name;
                variables["MREPO_FULL_NAME"] = await item.GetFullNameAsync(_itemOptionsProvider, _repositoryOptions);
                variables["MREPO_WORKSTEAD_NAME"] = item.Record.GetWorksteadName();

                if (item.Record.Type is RecordType.Project)
                {
                    variables["MREPO_PROJECT_PATH"] = ProjectStrategyHelper.GetProjectFilePath(item, _fileSystem);
                }
            }
        }

        public async Task<int> ExecuteScriptAsync(
            string script,
            string? workingDirectory = null,
            Func<IDictionary<string, string?>, Task>? setEnvironmentVariables = null,
            CancellationToken cancellationToken = default)
        {
            var arguments = script.Replace("\"", "\"\"\"");
            workingDirectory = workingDirectory.NormalizePath();

            ProcessStartInfo startInfo = new(_repositoryOptions.Shell ?? "powershell", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };

            if (setEnvironmentVariables is not null)
            {
                await setEnvironmentVariables(startInfo.Environment);
            }

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

        private string PrepareScriptToExecute(string script, IScriptContext context)
        {
            // TODO: [P2] Prepare script with arguments (parameter replacement, check for parameters)
            return script + ' ' + string.Join(' ', context.Args);
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
                var node = tree.GetOrCreateTargetNode(itemOptions.Path);
                node?.AddScripts(itemOptions.Scripts);
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
