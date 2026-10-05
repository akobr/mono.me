using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Registered only with AddAzureAdMachineAccess: machine app registrations need the directory,
// the API application they get roles on, and the two app roles (read, read/write).
public sealed class AzureAdMachineAccessOptionsValidator : IValidateOptions<UserAuthenticationOptions>
{
    private readonly IOptions<AzureAdMachineAccessOptions> _machineOptions;

    public AzureAdMachineAccessOptionsValidator(IOptions<AzureAdMachineAccessOptions> machineOptions)
    {
        _machineOptions = machineOptions;
    }

    public ValidateOptionsResult Validate(string? name, UserAuthenticationOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(_machineOptions.Value.TenantId))
        {
            failures.Add($"{AzureAdMachineAccessOptions.SectionName}:TenantId is required for Entra ID machine access.");
        }

        if (string.IsNullOrWhiteSpace(options.TenantId) || string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Auth:TenantId and Auth:ClientId (the Storyteller API application) are required for Entra ID machine access.");
        }

        foreach (var role in new[] { AzureAdMachineAccessService.ReadRoleKey, AzureAdMachineAccessService.ReadWriteRoleKey })
        {
            if (!options.AppRoles.TryGetValue(role, out var roleId) || !Guid.TryParse(roleId, out _))
            {
                failures.Add($"Auth:AppRoles:{role} must be the ID of the API app role for Entra ID machine access.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
