using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Finds the bindings of a document as the resolver does (Binding.Object ConfigurationBindingResolver): every string that
// starts with '@', and every object whose $binding is a string with a known kind or next to a $definition. The $context
// of an object binding is walked, because the resolver binds it first; $definition is the program and is not.
public static class BindingScanner
{
    public const string BindingProperty = "$binding";
    public const string DefinitionProperty = "$definition";
    public const string ContextProperty = "$context";

    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal) { "jlogic", "jsone" };
    private static readonly HashSet<string> EnvelopeProperties = new(StringComparer.Ordinal) { BindingProperty, DefinitionProperty, ContextProperty };

    public static IReadOnlyList<BindingOccurrence> Scan(JToken document)
    {
        var found = new List<BindingOccurrence>();
        Visit(document, string.Empty, null, found);
        return found;
    }

    // An object binding the resolver evaluates, or one it refuses (422): a known kind, or a $definition next to a $binding.
    public static bool IsObjectBinding(JObject obj, out string kind, out bool isValid)
    {
        kind = string.Empty;
        isValid = false;

        if (obj.Property(BindingProperty, StringComparison.Ordinal)?.Value is not JValue { Type: JTokenType.String } binding)
        {
            return false;
        }

        kind = binding.Value<string>() ?? string.Empty;
        var known = KnownKinds.Contains(kind);
        var hasDefinition = obj.Property(DefinitionProperty, StringComparison.Ordinal) is not null;
        isValid = known && hasDefinition && obj.Properties().All(property => EnvelopeProperties.Contains(property.Name));
        return known || hasDefinition;
    }

    private static void Visit(JToken token, string pointer, string? envelope, List<BindingOccurrence> found)
    {
        switch (token)
        {
            case JValue { Type: JTokenType.String } value when BindingExpressions.IsBinding(value.Value<string>()):
            {
                var expression = value.Value<string>()!;
                var parsed = BindingExpressions.Parse(expression);
                found.Add(new BindingOccurrence(pointer, expression, parsed, parsed.Form != BindingForm.Invalid, envelope));
                break;
            }

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    Visit(array[index], JsonPointer.Append(pointer, index), envelope, found);
                }

                break;

            case JObject obj when IsObjectBinding(obj, out var kind, out var isValid):
            {
                found.Add(new BindingOccurrence(pointer, kind, new ParsedBinding(BindingForm.Object, kind, [], false, false), isValid, envelope));

                if (obj.Property(ContextProperty, StringComparison.Ordinal)?.Value is { } context)
                {
                    Visit(context, JsonPointer.Append(pointer, ContextProperty), envelope ?? pointer, found);
                }

                break;
            }

            case JObject obj:
                foreach (var property in obj.Properties())
                {
                    Visit(property.Value, JsonPointer.Append(pointer, property.Name), envelope, found);
                }

                break;
        }
    }
}
