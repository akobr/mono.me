using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Model.Items;
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
            ProcessStartInfo startInfo = new(repositoryOptions.Shell ?? "powershell", script)
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

            var tree = new ScriptTree(
                new ScriptTreeNode(repository.Name, null, repositoryOptions.Scripts));

            foreach (var itemOptions in itemOptionsProvider.GetAllOptions())
            {
                var itemNode = tree.GetOrCreateTargetNode(itemOptions.Path);

                if (itemNode is null)
                {
                    continue;
                }
            }

            return tree;
        }
    }
}
