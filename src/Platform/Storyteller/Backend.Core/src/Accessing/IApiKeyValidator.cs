using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface IApiKeyValidator
{
    Task<ApiKeyValidationResult?> ValidateAsync(string rawApiKey);
}
