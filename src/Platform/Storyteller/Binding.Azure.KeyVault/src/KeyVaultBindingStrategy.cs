using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Azure;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public class KeyVaultBindingStrategy(IAzureClientFactory<SecretClient> factory, string bindingKey)
    : IBindingStrategy
{
    private readonly SecretClient _client = factory.CreateClient(bindingKey);

    public async ValueTask<bool> TryBinding(BindingContext context)
    {
        if (!context.IncludeSecrets)
        {
            return false;
        }

        var secretName = context.Path.Replace(".", "--");
        var secretResponse = await _client.GetSecretAsync(secretName);

        if (!secretResponse.HasValue)
        {
            return false;
        }

        var secret = secretResponse.Value.Value;
        context.Property.Value = JRaw.CreateString(secret);
        return true;
    }
}
