using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.Hosting;

// Fails the start on settings that would let the server run without a pinned tenant, without a known client, without
// a credential for on-behalf-of, or with tokens over plain HTTP outside Development.
public sealed class McpHostOptionsValidator(IHostEnvironment environment) : IValidateOptions<McpHostOptions>
{
    public const string AuthKitRefusal =
        "The MCP server signs in with Entra ID only: AuthKit issues no token for the Storyteller API on behalf of a user. "
        + "See docs/Platform/Storyteller/mcp.md.";

    public ValidateOptionsResult Validate(string? name, McpHostOptions options)
    {
        if (options.Provider == McpIdentityProvider.AuthKit)
        {
            return ValidateOptionsResult.Fail(AuthKitRefusal);
        }

        if (!Enum.IsDefined(options.Provider))
        {
            return ValidateOptionsResult.Fail($"Mcp:Provider '{options.Provider}' is not a known identity provider.");
        }

        var failures = new List<string>();
        var entra = options.EntraId;

        if (!McpUrls.IsValid(options.ResourceUrl, environment, out var resourceProblem))
        {
            failures.Add($"Mcp:ResourceUrl {resourceProblem}");
        }

        if (!Guid.TryParse(entra.TenantId, out _))
        {
            failures.Add("Mcp:EntraId:TenantId must be the GUID of the tenant (not common or organizations).");
        }

        if (!Guid.TryParse(entra.ClientId, out _))
        {
            failures.Add("Mcp:EntraId:ClientId must be the GUID of the MCP app registration.");
        }

        if (entra.AllowedClientIds.Count == 0 || entra.AllowedClientIds.Any(id => !Guid.TryParse(id, out _)))
        {
            failures.Add("Mcp:EntraId:AllowedClientIds must list the GUIDs of the MCP client apps whose tokens the server accepts.");
        }

        if (string.IsNullOrWhiteSpace(entra.Scope) || entra.Scope.Contains(' ', StringComparison.Ordinal))
        {
            failures.Add("Mcp:EntraId:Scope must be one scope name, for example Mcp.Access.");
        }

        if (!string.IsNullOrWhiteSpace(entra.Audience) && entra.Audience.EndsWith('/'))
        {
            failures.Add("Mcp:EntraId:Audience must not end with a slash.");
        }

        var credentials = (string.IsNullOrWhiteSpace(entra.ClientSecret) ? 0 : 1)
            + (string.IsNullOrWhiteSpace(entra.CertificatePath) ? 0 : 1)
            + (entra.UseManagedIdentity ? 1 : 0);

        if (credentials != 1)
        {
            failures.Add("Set exactly one credential of the MCP app: Mcp:EntraId:ClientSecret, Mcp:EntraId:CertificatePath or Mcp:EntraId:UseManagedIdentity.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
