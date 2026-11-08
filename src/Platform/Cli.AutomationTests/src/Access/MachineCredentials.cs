using System.Text;

namespace _42.Platform.Cli.AutomationTests.Access;

// The STORYTELLER_* variables of a machine sign-in (the names of the SDK's StorytellerVariables and the CLI's
// TargetVariables), for SformRunOptions.Machine: a pre-provisioned machine of Machines:<name> (FromSettings) or one a
// test created (CreatedMachine.Credentials, or ForApiKey, ForClientCredentials, ForCertificate, ForFederated,
// FromVariables). The values that are secrets are listed in Secrets, so the runner masks them in its attachments.
public sealed class MachineCredentials
{
    public const string Endpoint = "STORYTELLER_ENDPOINT";
    public const string ApiKeyVariable = "STORYTELLER_API_KEY";
    public const string ClientCertificate = "STORYTELLER_CLIENT_CERTIFICATE";
    public const string ClientCertificatePassword = "STORYTELLER_CLIENT_CERTIFICATE_PASSWORD";
    public const string ClientIdVariable = "STORYTELLER_CLIENT_ID";
    public const string ClientSecret = "STORYTELLER_CLIENT_SECRET";
    public const string TokenUrl = "STORYTELLER_TOKEN_URL";
    public const string TokenScope = "STORYTELLER_TOKEN_SCOPE";
    public const string TokenFile = "STORYTELLER_TOKEN_FILE";
    public const string Project = "STORYTELLER_PROJECT";
    public const string View = "STORYTELLER_VIEW";
    public const string Key = "STORYTELLER_KEY";

    private MachineCredentials(string name, IReadOnlyDictionary<string, string?> variables, IReadOnlyList<string> secrets, string? machineId)
    {
        Name = name;
        Variables = variables;
        Secrets = secrets;
        MachineId = machineId;
    }

    // Machines:<name>, or a description of a created machine.
    public string Name { get; }

    // The variables to set (null values remove a variable).
    public IReadOnlyDictionary<string, string?> Variables { get; }

    // The values to mask: the API key, the client secret, the certificate password.
    public IReadOnlyList<string> Secrets { get; }

    // The machine id, when known (from the settings or the API key).
    public string? MachineId { get; }

    // The STORYTELLER_PROJECT it targets, when set.
    public string? ProjectKey => Variables.TryGetValue(Project, out var project) ? project : null;

    public static MachineCredentials FromSettings(string name, MachineSettings machine, string? defaultEndpoint, Func<string, string>? resolvePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(machine);

        var variables = new Dictionary<string, string?>(StringComparer.Ordinal);
        var secrets = new List<string>();
        Set(variables, Endpoint, machine.Endpoint ?? defaultEndpoint);
        Set(variables, ApiKeyVariable, machine.ApiKey, secrets);
        Set(variables, ClientIdVariable, machine.ClientId);
        Set(variables, ClientSecret, machine.ClientSecret, secrets);
        Set(variables, TokenUrl, machine.ClientCredentialsUrl);
        Set(variables, TokenScope, machine.ClientCredentialsScope);
        Set(variables, ClientCertificate, machine.Certificate is { Length: > 0 } certificate && resolvePath is not null ? resolvePath(certificate) : machine.Certificate);
        Set(variables, ClientCertificatePassword, machine.CertificatePassword, secrets);
        Set(variables, Project, machine.Project);
        Set(variables, View, machine.View);
        Set(variables, Key, machine.Key);
        return new MachineCredentials($"Machines:{name}", variables, secrets, machine.Id ?? MachineIdOf(machine.ApiKey));
    }

    // A machine with an API key, e.g. one a test created with sform account machine create.
    public static MachineCredentials ForApiKey(string apiKey, string endpoint, string? projectKey = null, string? view = null, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal);
        Set(variables, Endpoint, endpoint);
        Set(variables, ApiKeyVariable, apiKey);
        Set(variables, Project, projectKey);
        Set(variables, View, view);
        var machineId = MachineIdOf(apiKey);
        return new MachineCredentials(name ?? $"machine {machineId ?? "with an API key"}", variables, [apiKey], machineId);
    }

    public static MachineCredentials ForClientCredentials(string clientId, string clientSecret, string endpoint, string projectKey, string? tokenUrl = null, string? tokenScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal);
        Set(variables, Endpoint, endpoint);
        Set(variables, ClientIdVariable, clientId);
        Set(variables, ClientSecret, clientSecret);
        Set(variables, TokenUrl, tokenUrl);
        Set(variables, TokenScope, tokenScope);
        Set(variables, Project, projectKey);
        return new MachineCredentials($"machine {clientId}", variables, [clientSecret], clientId);
    }

    public static MachineCredentials ForCertificate(string certificatePath, string? password, string endpoint, string? projectKey = null, string? apiKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal);
        var secrets = new List<string>();
        Set(variables, Endpoint, endpoint);
        Set(variables, ClientCertificate, certificatePath);
        Set(variables, ClientCertificatePassword, password, secrets);
        Set(variables, ApiKeyVariable, apiKey, secrets);
        Set(variables, Project, projectKey);
        return new MachineCredentials($"machine with the certificate {Path.GetFileName(certificatePath)}", variables, secrets, MachineIdOf(apiKey));
    }

    // A federated machine (workload identity federation): its id as the client id and the file of the platform token
    // the test signed (MCH-FED-05 … 08); no secret of its own (the token file is the test's). endpoint: the
    // STORYTELLER_ENDPOINT the creation printed (CreatedMachine.PrintedVariables) or Target:BaseUrl.
    public static MachineCredentials ForFederated(string clientId, string tokenFile, string endpoint, string projectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenFile);
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal);
        Set(variables, Endpoint, endpoint);
        Set(variables, ClientIdVariable, clientId);
        Set(variables, TokenFile, tokenFile);
        Set(variables, Project, projectKey);
        return new MachineCredentials($"federated machine {clientId}", variables, [], clientId);
    }

    // Any combination of variables (the names of the constants above, or others such as STORYTELLER_SUBJECT_TOKEN_TYPE;
    // null removes one), with the values to mask, e.g. a case of variables the factories do not build.
    public static MachineCredentials FromVariables(string name, IReadOnlyDictionary<string, string?> variables, IEnumerable<string>? secrets = null, string? machineId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(variables);
        var copy = new Dictionary<string, string?>(variables, StringComparer.Ordinal);
        var apiKey = copy.TryGetValue(ApiKeyVariable, out var key) ? key : null;
        return new MachineCredentials(
            name,
            copy,
            [.. (secrets ?? []).Where(secret => !string.IsNullOrWhiteSpace(secret))],
            machineId ?? MachineIdOf(apiKey));
    }

    // The same machine with other variables on top (null removes one), e.g. a wrong key or another STORYTELLER_PROJECT.
    public MachineCredentials With(IReadOnlyDictionary<string, string?> changes, IEnumerable<string>? moreSecrets = null)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var variables = new Dictionary<string, string?>(Variables, StringComparer.Ordinal);
        foreach (var (name, value) in changes)
        {
            variables[name] = value;
        }

        return new MachineCredentials(Name, variables, [.. Secrets, .. moreSecrets ?? []], MachineId);
    }

    // The machine id of a structured API key, 2s.<base64url of organization:project:machine id>.<secret>; null for a key
    // in another format.
    public static string? MachineIdOf(string? apiKey)
    {
        return DecodeSegment(apiKey) is { Length: 3 } parts ? parts[2] : null;
    }

    // "<organization>.<project>" of a structured API key; null for a key in another format.
    public static string? ProjectKeyOf(string? apiKey)
    {
        return DecodeSegment(apiKey) is { Length: 3 } parts ? $"{parts[0]}.{parts[1]}" : null;
    }

    private static string[]? DecodeSegment(string? apiKey)
    {
        var key = apiKey?.Trim();
        if (key is null)
        {
            return null;
        }

        if (key.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
        {
            key = key[7..].Trim();
        }

        var segments = key.Split('.');
        if (segments.Length != 3 || segments[0] != "2s")
        {
            return null;
        }

        try
        {
            var base64 = segments[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split(':');
            return parts.Length == 3 && parts.All(part => part.Length > 0) ? parts : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void Set(Dictionary<string, string?> variables, string name, string? value, List<string>? secrets = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        variables[name] = value.Trim();
        secrets?.Add(value.Trim());
    }
}
