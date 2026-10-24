using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.Hosting;

public sealed class StorytellerApiOptionsValidator(IHostEnvironment environment) : IValidateOptions<StorytellerApiOptions>
{
    public ValidateOptionsResult Validate(string? name, StorytellerApiOptions options)
    {
        var failures = new List<string>();

        if (!McpUrls.IsValid(options.BaseUrl, environment, out var problem))
        {
            failures.Add($"Storyteller:BaseUrl {problem}");
        }

        if (options.Scopes.Count == 0 || options.Scopes.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Storyteller:Scopes must list the delegated scopes of the API, for example api://{ApiClientId}/User.Impersonation.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
