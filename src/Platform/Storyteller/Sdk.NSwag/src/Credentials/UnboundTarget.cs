using System;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// What an unbound credential falls back to: the endpoint of STORYTELLER_ENDPOINT (or STORYTELLER_API_URL), and the
// organization and project of STORYTELLER_KEY, split as @{organization}.{project}.… without parsing the rest.
internal static class UnboundTarget
{
    public static Uri? ReadEndpoint(Func<string, string?> getVariable)
    {
        var variables = new EnvironmentVariables(getVariable);
        var endpoint = variables.ReadWithFallback(StorytellerVariables.ENDPOINT, StorytellerVariables.API_URL, out var source);

        return endpoint is null ? null : LocalUrlRule.Require(endpoint, source);
    }

    public static (string? Organization, string? Project) ReadProject(Func<string, string?> getVariable)
    {
        var key = new EnvironmentVariables(getVariable).Read(StorytellerVariables.KEY);

        if (key is null)
        {
            return (null, null);
        }

        var segments = key.StartsWith('@') ? key[1..].Split('.') : [];

        return segments.Length >= 3 && segments[0].Length > 0 && segments[1].Length > 0
            ? (segments[0], segments[1])
            : throw StorytellerAuthenticationException.Configuration(
                $"{StorytellerVariables.KEY} must be a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}.");
    }
}
