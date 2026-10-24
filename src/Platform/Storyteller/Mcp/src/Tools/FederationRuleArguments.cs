using System.Text.Json;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Federation rules arrive as JSON and become SDK rules member by member. Only the four members of a rule are accepted,
// as sform does: a misspelled Claims would otherwise travel as an extra property and leave a rule that admits more
// tokens than the person meant. The API validates the content of each rule.
public static class FederationRuleArguments
{
    public const string Shape =
        "[{\"Issuer\": \"<trusted issuer name>\", \"Audience\": \"<aud>\", \"Subject\": \"<sub, or a prefix ending with :* or /*>\", "
        + "\"Claims\": {\"<claim name or /json/pointer>\": [\"<accepted value>\"]}}]";

    private static readonly string[] Members = ["Issuer", "Audience", "Subject", "Claims"];

    public static List<Sdk.FederationRule> Parse(JsonElement element, string parameter)
    {
        var rules = ToolArguments.ToArray(element, parameter);
        var result = new List<Sdk.FederationRule>(rules.Count);

        for (var index = 0; index < rules.Count; index++)
        {
            var name = $"{parameter}[{index}]";

            if (rules[index] is not JObject rule)
            {
                throw new ToolInputException($"'{name}' must be a JSON object: {Shape}.");
            }

            var members = ReadMembers(rule, name);
            result.Add(new Sdk.FederationRule
            {
                Issuer = ReadText(members, "Issuer", name),
                Audience = ReadText(members, "Audience", name),
                Subject = ReadText(members, "Subject", name),
                Claims = ReadClaims(members.GetValueOrDefault("Claims"), name),
            });
        }

        return result;
    }

    private static Dictionary<string, JToken> ReadMembers(JObject rule, string name)
    {
        var members = new Dictionary<string, JToken>(StringComparer.Ordinal);

        foreach (var property in rule.Properties())
        {
            var member = Members.FirstOrDefault(candidate => string.Equals(candidate, property.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ToolInputException($"'{name}' has the unknown member '{property.Name}'; a rule has only Issuer, Audience, Subject and Claims.");

            if (!members.TryAdd(member, property.Value))
            {
                throw new ToolInputException($"'{name}' has {member} twice.");
            }
        }

        return members;
    }

    // A missing member stays null, and the API names it in its answer.
    private static string? ReadText(Dictionary<string, JToken> members, string member, string name)
    {
        if (!members.TryGetValue(member, out var token) || token.Type == JTokenType.Null)
        {
            return null;
        }

        return token.Type == JTokenType.String
            ? token.Value<string>()
            : throw new ToolInputException($"'{name}.{member}' must be a JSON string.");
    }

    private static Dictionary<string, Sdk.IReadOnlyList_String>? ReadClaims(JToken? token, string name)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        if (token is not JObject claims)
        {
            throw new ToolInputException($"'{name}.Claims' must be a JSON object of claim names and their accepted values.");
        }

        var result = new Dictionary<string, Sdk.IReadOnlyList_String>(StringComparer.Ordinal);

        foreach (var claim in claims.Properties())
        {
            var values = new Sdk.IReadOnlyList_String();
            var items = claim.Value is JArray array ? array.ToList() : [claim.Value];

            foreach (var item in items)
            {
                // Accepted values are strings, numbers included ("65"): the API compares a claim's JSON text with them.
                if (item.Type != JTokenType.String)
                {
                    throw new ToolInputException($"'{name}.Claims.{claim.Name}' must hold strings, for example [\"65\"].");
                }

                values.Add(item.Value<string>()!);
            }

            result[claim.Name] = values;
        }

        return result;
    }
}
