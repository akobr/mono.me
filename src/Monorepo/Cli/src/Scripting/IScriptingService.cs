using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Scripting
{
    public interface IScriptingService
    {
        bool HasScript(IScriptContext context);

        IEnumerable<string> GetAvailableScriptNames(IItem item);

        Task<int> ExecuteScriptAsync(IScriptContext context, CancellationToken cancellationToken = default);

        Task<int> ExecuteScriptAsync(string script, string? workingDirectory = null, CancellationToken cancellationToken = default);
    }
}
