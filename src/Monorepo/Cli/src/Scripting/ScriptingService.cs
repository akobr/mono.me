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
        private readonly IItemOptionsProvider itemOptionsProvider;
        private readonly ITypeOptionsProvider typeOptionsProvider;
        private readonly ScriptTree scriptTree;

        public ScriptingService(
            IOptions<MonoRepoOptions> repositoryOptions,
            IItemOptionsProvider itemOptionsProvider,
            ITypeOptionsProvider typeOptionsProvider)
        {
            this.repositoryOptions = repositoryOptions.Value;
            this.itemOptionsProvider = itemOptionsProvider;
            this.typeOptionsProvider = typeOptionsProvider;
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
            ProcessStartInfo startInfo = new("powershell", script)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            };

            var process = Process.Start(startInfo);

            if (process is null
                || process.HasExited)
            {
                return ExitCodes.WARNING_ABORTED;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
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

                itemNode.AddScripts(itemOptions.Scripts);

                if (!string.IsNullOrEmpty(itemOptions.Type))
                {
                    var typeOptions = typeOptionsProvider.GetOptions(itemOptions.Type);
                    itemNode.AddScripts(typeOptions.Scripts);
                }
            }

            return tree;
        }
    }
}
