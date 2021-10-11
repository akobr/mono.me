using System.Collections.Generic;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Scripting
{
    public interface IScriptContext
    {
        string ScriptName { get; }

        IItem Item { get; }

        List<string> Args { get; }
    }
}
