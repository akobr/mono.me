using System.Net;
using Azure;
using Azure.Security.KeyVault.Secrets;

namespace _42.Utils.Configuration.Substitute.KeyVault;

public class KeyVaultSubstituteStrategy(SecretClient client) : ISubstituteStrategy
{
    public string? Substitute(string propertyPath)
    {
        try
        {
            var response = client.GetSecret(propertyPath);
            return response.HasValue ? response.Value.Value : null;
        }
        catch (RequestFailedException requestFailedException)
        {
            if (requestFailedException.Status == (int)HttpStatusCode.NotFound)
            {
                return null;
            }

            throw;
        }
    }
}
