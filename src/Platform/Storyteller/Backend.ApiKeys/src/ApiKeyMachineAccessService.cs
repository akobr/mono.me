using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller;

public class ApiKeyMachineAccessService : IMachineAccessService, IApiKeyValidator
{
    private const int SecretSizeInBytes = 32;

    private readonly IApiKeyHashStore _hashStore;

    public ApiKeyMachineAccessService(IApiKeyHashStore hashStore)
    {
        _hashStore = hashStore;
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var id = Guid.NewGuid().ToString("D");
        var secret = GenerateSecret();
        var scope = model.Scope;
        var structuredKey = new StructuredApiKey(model.Organization, model.Project, id, secret);
        var rawKey = structuredKey.Format();

        await _hashStore.StoreAsync(
            model.Organization, model.Project, id, HashSecret(secret), scope);

        return new MachineAccess
        {
            Id = id,
            ObjectId = id,
            AccessKey = rawKey,
            AnnotationKey = model.AnnotationKey,
            Scope = scope,
        };
    }

    public async Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
    {
        var existing = await _hashStore.GetAsync(organization, project, objectId);

        if (existing is null)
        {
            return null;
        }

        var secret = GenerateSecret();
        var structuredKey = new StructuredApiKey(organization, project, objectId, secret);

        await _hashStore.StoreAsync(
            organization, project, objectId, HashSecret(secret), existing.Scope);

        return structuredKey.Format();
    }

    public async Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
    {
        return await _hashStore.DeleteAsync(organization, project, objectId);
    }

    public async Task<ApiKeyValidationResult?> ValidateAsync(string rawApiKey)
    {
        var parsed = StructuredApiKey.TryParse(rawApiKey);

        if (parsed is null)
        {
            return null;
        }

        var entry = await _hashStore.GetAsync(parsed.Organization, parsed.Project, parsed.MachineAccessId);

        if (entry is null)
        {
            return null;
        }

        var presentedHash = HashSecret(parsed.Secret);
        var storedHashBytes = Encoding.UTF8.GetBytes(entry.HashedSecret);
        var presentedHashBytes = Encoding.UTF8.GetBytes(presentedHash);

        if (!CryptographicOperations.FixedTimeEquals(presentedHashBytes, storedHashBytes))
        {
            return null;
        }

        return new ApiKeyValidationResult(
            parsed.Organization,
            parsed.Project,
            parsed.MachineAccessId,
            entry.Scope);
    }

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretSizeInBytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
