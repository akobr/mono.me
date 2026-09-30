using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Json;

public static class JsonPatchBuilder
{
    /// <summary>
    /// Creates JSON Patch (RFC 6902) operations which turn the original document into the target document.
    /// </summary>
    /// <remarks>
    /// Objects are compared property by property, arrays and scalar values are replaced as a whole when they differ,
    /// so the result doesn't depend on the merge rules of the server (array union, ignored nulls).
    /// When a changed property name needs JSON Pointer escaping (<c>~</c> or <c>/</c>), the whole document is replaced by one operation,
    /// because the JSON Patch implementation of the server doesn't decode escaped pointers.
    /// </remarks>
    /// <returns>The patch operations; empty when the documents are equal.</returns>
    public static JArray Create(JObject original, JObject target)
    {
        var operations = new JArray();
        AddObjectOperations(original, target, string.Empty, operations);

        if (operations.Any(operation => ((string)operation["path"]!).Contains('~')))
        {
            return [CreateValueOperation("replace", string.Empty, target)];
        }

        return operations;
    }

    private static void AddObjectOperations(JObject original, JObject target, string path, JArray operations)
    {
        foreach (var property in original.Properties())
        {
            if (target.Property(property.Name, StringComparison.Ordinal) is null)
            {
                operations.Add(new JObject
                {
                    ["op"] = "remove",
                    ["path"] = GetPropertyPath(path, property.Name),
                });
            }
        }

        foreach (var property in target.Properties())
        {
            var propertyPath = GetPropertyPath(path, property.Name);
            var originalProperty = original.Property(property.Name, StringComparison.Ordinal);

            if (originalProperty is null)
            {
                operations.Add(CreateValueOperation("add", propertyPath, property.Value));
            }
            else if (originalProperty.Value is JObject originalObject && property.Value is JObject targetObject)
            {
                AddObjectOperations(originalObject, targetObject, propertyPath, operations);
            }
            else if (!JToken.DeepEquals(originalProperty.Value, property.Value))
            {
                operations.Add(CreateValueOperation("replace", propertyPath, property.Value));
            }
        }
    }

    private static JObject CreateValueOperation(string operation, string path, JToken value)
    {
        return new JObject
        {
            ["op"] = operation,
            ["path"] = path,
            ["value"] = value.DeepClone(),
        };
    }

    private static string GetPropertyPath(string parentPath, string propertyName)
    {
        // JSON Pointer (RFC 6901) escaping
        return $"{parentPath}/{propertyName.Replace("~", "~0").Replace("/", "~1")}";
    }
}
