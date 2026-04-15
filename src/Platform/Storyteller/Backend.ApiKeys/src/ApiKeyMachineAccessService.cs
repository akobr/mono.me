using System.Security.Cryptography;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller;

public class ApiKeyMachineAccessService : IMachineAccessService
{
    private const int KeySizeInBytes = 48;

    public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var id = Guid.NewGuid().ToString();
        var rawKey = GenerateApiKey();

        var machineAccess = new MachineAccess
        {
            Id = id,
            ObjectId = id,
            AccessKey = rawKey,
            AnnotationKey = model.AnnotationKey,
            Scope = model.Scope <= MachineAccessScope.ConfigurationRead
                ? MachineAccessScope.DefaultRead
                : MachineAccessScope.DefaultReadWrite,
        };

        return Task.FromResult(machineAccess);
    }

    public Task<string?> ResetMachineAccessAsync(string objectId)
    {
        var rawKey = GenerateApiKey();
        return Task.FromResult<string?>(rawKey);
    }

    public Task<bool> DeleteMachineAccessAsync(string objectId)
    {
        // No external resource to clean up for API key auth.
        return Task.FromResult(true);
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(KeySizeInBytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
