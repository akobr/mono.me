using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Machines: the identities applications and pipelines call the API with. Not exposed: the token exchange (a machine
// flow), certificate renewal (a machine over mTLS), shared certificates, trusted issuers, invitations and members.
[McpServerToolType]
public sealed class MachineTools(Sdk.IAccessApiClient access)
{
    [McpServerTool(Name = "list_machines", Title = "List machines", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the machines of a project: Id, Scopes, CredentialKind, the annotation restriction (AnnotationKey), the "
        + "certificate expiry and, for Federated machines, FederationRules and TokenEndpoint. Secrets are masked by the API "
        + "(AccessKey keeps its first three characters); certificates and their passwords are never returned. Needs "
        + "Contributor on the project.")]
    public async Task<CallToolResult> ListMachines(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.Limit)] int? limit = null,
        [Description(ToolText.ContinuationToken)] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        // The API returns every machine at once; the host pages through that one list.
        var cursor = HostPage.Decode(continuationToken);
        var machines = await access.GetMachineAccessesAsync(organization, project, cancellationToken);
        return ToolResults.Json(HostPage.Take(machines?.ToList() ?? [], null, cursor, HostPage.ClampLimit(limit)));
    }

    [McpServerTool(Name = "get_machine", Title = "Get a machine", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns one machine as list_machines shows it: secrets masked, no certificate. Needs Contributor on the project.")]
    public async Task<CallToolResult> GetMachine(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(MachineText.Id)] string id,
        CancellationToken cancellationToken)
    {
        return ToolResults.Json(await access.GetMachineAccessAsync(organization, project, ToolArguments.Required(id, nameof(id)), cancellationToken));
    }

    [McpServerTool(Name = "create_machine", Title = "Create a machine", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Creates a machine, the identity an application or pipeline calls the API with. Its scopes decide what it may do and "
        + "cannot change later; the API adds the read scope of each write scope. " + MachineText.Roles + " "
        + "Prefer the credential kind Federated for a workload that has its own identity (GitHub Actions, Kubernetes, Azure "
        + "managed identity, AWS, Google Cloud, Vercel): it has no secret, and its federation rules decide which platform "
        + "tokens it may exchange at its TokenEndpoint (trusted issuers are set up by an Administrator with sform or the "
        + "admin UI). The other kinds return one-time credentials: an API key or a client secret in AccessKey, a base64 "
        + "PKCS#12 Certificate with its CertificatePassword, a TokenEndpoint and TokenScope for ClientCredentials. The API "
        + "keeps no copy; CredentialNotice names what the result holds. The credentials enter the conversation. The server "
        + "never retries a creation; after a failed or interrupted call, check list_machines before creating again.")]
    public async Task<CallToolResult> CreateMachine(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(
            "The scopes of the machine by name, at least one runtime scope. Runtime: " + MachineScopeNames.Runtime + ". "
            + "Management (Administrator; each needs its runtime scope too): " + MachineScopeNames.Management + ". The scope "
            + "strings (Annotation.Read) are accepted too.")]
        string[] scopes,
        [Description("Restricts the machine to this annotation and its descendants; omit it for the whole project. " + ToolText.AnnotationKey)]
        string? annotationKey = null,
        [Description(
            "The credential kind: " + MachineScopeNames.CredentialKinds + ". It must be enabled in the project "
            + "(get_machine_authentication_policy); omit it for the project default, or Federated when federationRules are given.")]
        string? credentialKind = null,
        [Description("The lifetime of the machine's own certificate in days (Certificate, CertificateAndApiKey); omit it for the project default. The API clamps it to the deployment's bounds.")]
        int? certificateLifetimeDays = null,
        [Description("true creates a CertificateAndApiKey machine without a certificate of its own: it signs in with a shared certificate of the project and its API key.")]
        bool? useSharedCertificate = null,
        [Description(MachineText.FederationRules + " Required for Federated, refused for other kinds.")]
        JsonElement? federationRules = null,
        CancellationToken cancellationToken = default)
    {
        var rules = federationRules is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element
            ? FederationRuleArguments.Parse(element, nameof(federationRules))
            : null;

        // As in sform, rules imply the Federated kind. An empty list is no rules, as the API takes it.
        if (rules is { Count: 0 })
        {
            rules = null;
        }

        var kind = EnumNames.ParseOptional<Sdk.MachineAccessCreateCredentialKind>(credentialKind, nameof(credentialKind))
            ?? (rules is null ? null : Sdk.MachineAccessCreateCredentialKind.Federated);

        var body = new Sdk.MachineAccessCreate
        {
            Organization = organization,
            Project = project,
            Scopes = MachineScopeNames.Parse(scopes, nameof(scopes)),
            AnnotationKey = string.IsNullOrWhiteSpace(annotationKey) ? null : annotationKey.Trim(),
            CredentialKind = kind,
            CertificateLifetimeDays = certificateLifetimeDays,
            UseSharedCertificate = useSharedCertificate,
            FederationRules = rules,
        };

        var machine = await access.CreateMachineAccessAsync(organization, project, body, cancellationToken);
        return MachineCredentialNotice.Result(machine, MachineCredentialNotice.ForCreate(machine));
    }

    [McpServerTool(Name = "reset_machine", Title = "Reset the secret of a machine", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(
        "Replaces the secret of a machine (its API key, or its client secret for ClientCredentials) and returns the new one "
        + "once in AccessKey; the old secret stops working. The API keeps no copy; CredentialNotice names what the result "
        + "holds, and the secret enters the conversation. A Federated machine has no secret and cannot be reset (400). "
        + MachineText.CertificateReset + " A CertificateAndApiKey machine gets a new API key; the reset returns no "
        + "certificate. " + MachineText.Roles)]
    public async Task<CallToolResult> ResetMachine(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(MachineText.Id)] string id,
        CancellationToken cancellationToken)
    {
        var machineId = ToolArguments.Required(id, nameof(id));

        // The API has nothing to reset on a Certificate machine and answers 500, which no retry fixes.
        var existing = await access.GetMachineAccessAsync(organization, project, machineId, cancellationToken);

        if (existing.CredentialKind == Sdk.MachineAccessCredentialKind.Certificate)
        {
            throw new ToolInputException(MachineText.CertificateReset);
        }

        var machine = await access.ResetMachineAccessAsync(organization, project, machineId, cancellationToken);
        return MachineCredentialNotice.Result(machine, MachineCredentialNotice.ForReset(machine));
    }

    [McpServerTool(Name = "delete_machine", Title = "Delete a machine", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Deletes a machine for good: whatever signs in with its credentials stops working. A ClientCredentials machine's "
        + "client in the identity provider is deleted too, and a machine certificate is revoked. There is no restore. "
        + MachineText.Roles)]
    public async Task<CallToolResult> DeleteMachine(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(MachineText.Id)] string id,
        CancellationToken cancellationToken)
    {
        var machineId = ToolArguments.Required(id, nameof(id));
        await access.DeleteMachineAccessAsync(organization, project, machineId, cancellationToken);
        return ToolResults.Json(new { Id = machineId, Deleted = true });
    }

    [McpServerTool(Name = "set_machine_federation_rules", Title = "Replace the federation rules of a machine", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Replaces all federation rules of a Federated machine; they decide which workload's platform tokens the machine "
        + "exchanges for Storyteller tokens. The new rules apply to new exchanges; tokens already issued stay valid until they "
        + "expire. A machine of another kind answers 400 (MachineNotFederated). " + MachineText.Roles)]
    public async Task<CallToolResult> SetMachineFederationRules(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(MachineText.Id)] string id,
        [Description(MachineText.FederationRules)] JsonElement rules,
        CancellationToken cancellationToken)
    {
        var parsed = FederationRuleArguments.Parse(rules, nameof(rules));
        return ToolResults.Json(await access.SetMachineFederationRulesAsync(organization, project, ToolArguments.Required(id, nameof(id)), parsed, cancellationToken));
    }

    [McpServerTool(Name = "get_machine_authentication_policy", Title = "Get the machine authentication policy", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the machine authentication policy of a project: EnabledCredentialKinds, the default CredentialKind of new "
        + "machines and the default CertificateLifetimeDays, with the deployment defaults filled in. Needs Contributor on the project.")]
    public async Task<CallToolResult> GetMachineAuthenticationPolicy(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        CancellationToken cancellationToken)
    {
        return ToolResults.Json(await access.GetMachineAuthenticationAsync($"{organization}.{project}", cancellationToken));
    }

    [McpServerTool(Name = "set_machine_authentication_policy", Title = "Replace the machine authentication policy", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Replaces the machine authentication policy of a project. Disabling a credential kind stops every machine of that "
        + "kind at once (within a minute) until the kind is enabled again; machines keep their kind. Without credentialKind "
        + "the first enabled kind becomes the default; without certificateLifetimeDays the deployment default applies. "
        + "Needs Administrator on the project.")]
    public async Task<CallToolResult> SetMachineAuthenticationPolicy(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("The credential kinds machines of the project may use, at least one: " + MachineScopeNames.CredentialKinds + ".")]
        string[] enabledCredentialKinds,
        [Description("The default kind of new machines; one of enabledCredentialKinds.")]
        string? credentialKind = null,
        [Description("The default certificate lifetime of new machines in days, within the deployment's bounds (1 to 3,650 by default).")]
        int? certificateLifetimeDays = null,
        CancellationToken cancellationToken = default)
    {
        if (enabledCredentialKinds is null || enabledCredentialKinds.Length == 0)
        {
            throw new ToolInputException($"'{nameof(enabledCredentialKinds)}' needs at least one kind: {MachineScopeNames.CredentialKinds}.");
        }

        var body = new Sdk.MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = enabledCredentialKinds
                .Select(kind => EnumNames.Parse<Sdk.MachineCredentialKind>(kind, nameof(enabledCredentialKinds)))
                .Distinct()
                .ToList(),
            CredentialKind = EnumNames.ParseOptional<Sdk.MachineAuthenticationPolicyCredentialKind>(credentialKind, nameof(credentialKind)),
            CertificateLifetimeDays = certificateLifetimeDays,
        };

        return ToolResults.Json(await access.SetMachineAuthenticationAsync($"{organization}.{project}", body, cancellationToken));
    }
}
