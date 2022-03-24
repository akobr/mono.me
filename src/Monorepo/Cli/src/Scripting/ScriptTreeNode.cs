using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Monorepo.Cli.Scripting
{
    internal class ScriptTreeNode
    {
        public ScriptTreeNode(string name, ScriptTreeNode? parent, IDictionary<string, string> scripts)
        {
            Name = name;
            ScriptMap = new Dictionary<string, string>(scripts, StringComparer.OrdinalIgnoreCase);
            ChildrenMap = new Dictionary<string, ScriptTreeNode>(StringComparer.OrdinalIgnoreCase);
            Parent = parent;

            if (parent is not null)
            {
                parent.ChildrenMap[name] = this;
            }
        }

        public ScriptTreeNode(string name, ScriptTreeNode? parent)
            : this(name, parent, new Dictionary<string, string>())
        {
            // no operation
        }

        public string Name { get; }

        public ScriptTreeNode? Parent { get; }

        public Dictionary<string, ScriptTreeNode> ChildrenMap { get; }

        public Dictionary<string, string> ScriptMap { get; }

        public bool HasScript(string scriptName)
        {
            return ScriptMap.ContainsKey(scriptName)
                   || (Parent?.HasScript(scriptName) ?? false);
        }

        public IEnumerable<string> GetAvailableScriptNames()
        {
            return Parent is not null
                ? Parent.GetAvailableScriptNames().Concat(ScriptMap.Keys)
                : ScriptMap.Keys;
        }

        public string? GetScript(string scriptName)
        {
            return ScriptMap.TryGetValue(scriptName, out var script)
                ? script
                : Parent?.GetScript(scriptName);
        }

        public void AddScripts(IReadOnlyDictionary<string, string> scripts)
        {
            foreach (var (scriptName, script) in scripts)
            {
                ScriptMap[scriptName] = script;
            }
        }
    }
}
