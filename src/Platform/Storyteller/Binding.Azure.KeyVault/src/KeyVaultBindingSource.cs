using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Azure;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public class KeyVaultBindingSource(IAzureClientFactory<SecretClient> factory, string bindingKey)
    : IBindingSource
{
    private readonly SecretClient _client = factory.CreateClient(bindingKey);

    public async ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
    {
        if (!request.IncludeSecrets)
        {
            return null;
        }

        var secretName = string.Join("--", request.Path);
        var secretResponse = await _client.GetSecretAsync(secretName);

        if (!secretResponse.HasValue)
        {
            return null;
        }

        return new BindingValue(new JValue(secretResponse.Value.Value));
    }
}
