using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Registered only with AddAzureAdMachineAccess: machine app registrations need the directory and
// the API application they get roles on. The app role of each scope is optional, as creation checks
// the roles of the requested scopes, but one that is set must be an ID of its own.
public sealed class AzureAdMachineAccessOptionsValidator : IValidateOptions<UserAuthenticationOptions>
{
    // The keys of the removed presets, with the keys that replace them. No machine reads them any more,
    // so a deployment that still sets one fails the start instead of silently losing its roles.
    private static readonly Dictionary<string, MachineScope[]> RemovedRoleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DefaultRead"] = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
        ["DefaultReadWrite"] = [MachineScope.AnnotationRead, MachineScope.AnnotationWrite, MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite],
        ["SecretsReadWrite"] = [MachineScope.SecretsRead, MachineScope.SecretsWrite],
    };

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

        foreach (var (role, roleId) in options.AppRoles)
        {
            if (!string.IsNullOrWhiteSpace(roleId)
                && RemovedRoleKeys.TryGetValue(role, out var replacements))
            {
                failures.Add($"Auth:AppRoles:{role} was removed: Entra ID machines get one app role per scope. Set {string.Join(", ", replacements.Select(GetRoleSetting))} instead.");
            }
        }

        // One role per scope: with a shared ID, a machine would get a role whose value is another scope.
        var scopesByRoleId = new Dictionary<Guid, MachineScope>();

        foreach (var scope in MachineScopes.All)
        {
            if (!options.AppRoles.TryGetValue(AzureAdMachineAccessService.GetRoleKey(scope), out var configuredRoleId)
                || string.IsNullOrWhiteSpace(configuredRoleId))
            {
                continue;
            }

            if (!Guid.TryParse(configuredRoleId, out var roleId))
            {
                failures.Add($"{GetRoleSetting(scope)} must be the ID of the API app role {MachineScopes.ToScopeString(scope)} when it is set.");
            }
            else if (!scopesByRoleId.TryAdd(roleId, scope))
            {
                failures.Add($"{GetRoleSetting(scopesByRoleId[roleId])} and {GetRoleSetting(scope)} share the role ID {roleId}; every scope needs an app role of its own.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string GetRoleSetting(MachineScope scope)
    {
        return $"Auth:AppRoles:{AzureAdMachineAccessService.GetRoleKey(scope)}";
    }
}
