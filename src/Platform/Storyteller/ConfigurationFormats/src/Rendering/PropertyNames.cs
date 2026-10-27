using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.Rendering;

// The walk of the name rules of the formats: every property name in document order, also of a null or an empty
// container, as the base rules check them. It runs after DocumentFlattener.Read has passed.
internal static class PropertyNames
{
    // check: the name, its JSON path and whether it is a name of the root object; it throws on an invalid name.
    public static void Check(JToken? content, Action<string, string, bool> check)
    {
        if (content is JObject root)
        {
            Visit(root, JsonPaths.Root, true, check);
        }
    }

    private static void Visit(JToken token, string path, bool isRoot, Action<string, string, bool> check)
    {
        switch (token)
        {
            case JObject container:
                foreach (var property in container.Properties())
                {
                    var propertyPath = JsonPaths.Property(path, property.Name);
                    check(property.Name, propertyPath, isRoot);
                    Visit(property.Value, propertyPath, false, check);
                }

                break;

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    Visit(array[index], JsonPaths.Item(path, index), false, check);
                }

                break;
        }
    }
}
