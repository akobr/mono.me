#nullable enable

using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

/// <summary>
/// Stands in for <c>KeyVaultBindingSource</c>: a sourced <c>@</c> path resolves only when secrets are included.
/// </summary>
internal sealed class DecliningSecretSource : IBindingSource
{
    public const string VaultKey = "primaryVault";

    public const string SecretPath = "db.password";

    public const string SecretValue = "s3cret";

    public ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
    {
        if (!request.IncludeSecrets || request.PathString != SecretPath)
        {
            return new ValueTask<BindingValue?>((BindingValue?)null);
        }

        return new ValueTask<BindingValue?>(new BindingValue(new JValue(SecretValue)));
    }
}
