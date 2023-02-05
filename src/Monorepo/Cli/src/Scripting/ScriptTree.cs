using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _42.Monorepo.Cli.Scripting
{
    internal class ScriptTree
    {
        private readonly ScriptTreeNode _root;

        public ScriptTree(ScriptTreeNode root)
        {
            _root = root;
        }

        public static ScriptTree Empty { get; } = new(new ScriptTreeNode(string.Empty, null));

        public bool HasScript(string path, string scriptName)
        {
            return GetTargetNode(path)?.HasScript(scriptName) ?? false;
        }

        public IEnumerable<string> GetAvailableScriptNames(string path)
        {
            var segments = path.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            var targetNode = GetTargetNode(path);
            return targetNode is not null
                ? targetNode.GetAvailableScriptNames()
                : Enumerable.Empty<string>();
        }

        public string? GetScript(string path, string scriptName)
        {
            return GetTargetNode(path)?.GetScript(scriptName);
        }

        public ScriptTreeNode? GetOrCreateTargetNode(string path)
        {
            var segments = path.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            switch (segments.Length)
            {
                case < 1:
                case 2 when segments[0] == ".." && segments[1] == _root.Name:
                    return _root;

                case > 0 when segments[0] == "..":
                    return null;
            }

            var targetNode = _root;
            for (int i = 0, ln = segments.Length; i < ln && targetNode is not null; i++)
            {
                var segment = segments[i];

                switch (segment)
                {
                    case ".":
                        continue;

                    case "..":
                        targetNode = targetNode.Parent;
                        continue;
                }

                if (!targetNode.ChildrenMap.TryGetValue(segment, out var childNode))
                {
                    targetNode = new ScriptTreeNode(segment, targetNode);
                    continue;
                }

                targetNode = childNode;
            }

            return targetNode;
        }

        private ScriptTreeNode? GetTargetNode(string path)
        {
            var segments = path.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            switch (segments.Length)
            {
                case < 1:
                case 2 when segments[0] == ".." && segments[1] == _root.Name:
                    return _root;

                case > 0 when segments[0] == "..":
                    return null;
            }

            var targetNode = _root;
            for (int i = 0, ln = segments.Length; i < ln && targetNode is not null; i++)
            {
                var segment = segments[i];

                switch (segment)
                {
                    case ".":
                        continue;

                    case "..":
                        targetNode = targetNode.Parent;
                        continue;
                }

                if (!targetNode.ChildrenMap.TryGetValue(segment, out var childNode))
                {
                    break;
                }

                targetNode = childNode;
            }

            return targetNode;
        }
    }
}
