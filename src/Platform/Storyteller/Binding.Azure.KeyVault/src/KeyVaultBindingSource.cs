using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Azure;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public class KeyVaultBindingSource(IAzureClientFactory<SecretClient> factory, string bindingKey)
    : IBindingSource, IDescribedBinding
{
    private readonly SecretClient _client = factory.CreateClient(bindingKey);

    public BindingDescription Describe(string name)
    {
        List<BindingExample> examples =
        [
            new BindingExample
            {
                Expression = $"@(db.password, {name})",
                Description = "The secret `db--password`.",
            },
        ];

        if (name == "default")
        {
            examples.Add(new BindingExample
            {
                Expression = "@db.password",
                Description = "The same secret, read from the default source.",
            });
        }

        return new BindingDescription
        {
            Summary = $"A secret of the Azure Key Vault registered as `{name}`. The path becomes the secret name joined by `--`. Resolved only for callers that may read secrets.",
            IsSecret = true,
            Examples = examples,
        };
    }

    public async ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
    {
        if (!request.IncludeSecrets)
        {
            return null;
        }

        var secretName = string.Join("--", request.Path);

        try
        {
            var secretResponse = await _client.GetSecretAsync(secretName);

            if (!secretResponse.HasValue)
            {
                return null;
            }

            return new BindingValue(new JValue(secretResponse.Value.Value));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
