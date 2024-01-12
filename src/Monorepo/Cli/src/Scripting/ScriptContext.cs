using System.Collections.Generic;
using System.Diagnostics;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Scripting
{
    [DebuggerDisplay("{ScriptName}")]
    public class ScriptContext : IScriptContext
    {
        public ScriptContext(string scriptName, IItem item)
        {
            ScriptName = scriptName;
            Item = item;
            Args = new List<string>(0);
        }

        public string ScriptName { get; init; }

        public IItem Item { get; init; }

        public IReadOnlyList<string> Args { get; init; }
    }
}
