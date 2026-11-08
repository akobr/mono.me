using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests;

// Machines: a pre-provisioned one of Machines:<name> (AsMachine, the test marked [RequiresMachine("<name>")]), or one the
// test creates (CreateMachineAsync: sform account machine create as the leased user or another pool user, in the acting
// project or another, its printed secrets registered, deleted when the test ends). A machine's runs get an empty home of
// their own each, so nothing of a user reaches them.
public abstract partial class CliTest
{
    // The options of a run as the configured machine Machines:<name>; STORYTELLER_ENDPOINT defaults to Target:BaseUrl.
    protected SformRunOptions AsMachine(string name)
    {
        return AsMachine(MachineOf(name));
    }

    // The options of a run as the machine (e.g. CreatedMachine.Credentials).
    protected SformRunOptions AsMachine(MachineCredentials machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return new SformRunOptions { Machine = machine };
    }

    // Runs sform as the configured machine.
    protected Task<SformResult> SformAsMachine(string name, params string[] arguments)
    {
        return Sform(AsMachine(name), arguments);
    }

    // The credentials of Machines:<name>; throws with the setting to add when it is missing.
    protected MachineCredentials MachineOf(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Machines.TryGetValue(name, out var machine) || !machine.HasCredential)
        {
            throw new InvalidOperationException(
                $"Machines:{name} is not configured with a credential; set Machines:{name}:ApiKey (user secrets or UITESTS_Machines__{name}__ApiKey) and mark the test [RequiresMachine(\"{name}\")] so it skips without one.");
        }

        return MachineCredentials.FromSettings(name, machine, Target.BaseUrl, Settings.ResolvePath);
    }

    // Creates a machine in the acting project as the leased user (an Editor at least: Contributor, ContributorWithSecrets
    // for --scope Secrets*, Administrator for -m), with the options of sform account machine create (-r, -w, --scope, -m,
    // -k, -a, -o, -l, -s, --issuer, …; not -p and -v, which come from the acting project).
    protected Task<CreatedMachine> CreateMachineAsync(params string[] options)
    {
        return CreateMachineAsync(new MachineCreation(), options);
    }

    // Creates a machine as MachineCreation says (another pool user, another project, no cleanup). The printed secrets are
    // secrets of the test from then on; the machine is deleted when the test ends (by its creator; a machine that is gone
    // by then counts as deleted), registered before anything of the output is checked. Throws when sform does not
    // succeed; a test of a refusal runs the command itself.
    protected async Task<CreatedMachine> CreateMachineAsync(MachineCreation creation, params string[] options)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(options);

        var projectKey = creation.ProjectKey ?? ProjectKey;
        var view = creation.View ?? (creation.ProjectKey is null ? View : "default");
        var creator = creation.As;
        Task<SformResult> RunAsCreator(SformRunOptions runOptions, params string[] arguments)
        {
            return creator is null ? Sform(runOptions, arguments) : creator.Sform(runOptions, arguments);
        }

        var result = await RunAsCreator(
            new SformRunOptions { Label = creator is null ? null : $"as {creator}" },
            ["account", "machine", "create", .. options, "-p", projectKey, "-v", view]);

        // The id from the text first, so a machine that exists is deleted even when the JSON or a check below fails.
        var id = MachineIdPattern().Match(result.PlainStdOut) is { Success: true } idMatch ? idMatch.Groups["id"].Value : null;
        if (id is { } printedId && creation.DeleteAtCleanup)
        {
            OnCleanUp(() => DeleteMachineAsync(printedId, projectKey, view, RunAsCreator));
        }

        result.ShouldSucceed();
        var json = result.JsonNode();
        id ??= json["Id"]?.GetValue<string>() ?? throw result.Fail("printed a machine without an Id");

        var accessKey = Text(json["AccessKey"]);
        if (accessKey is not null)
        {
            AddSecret(accessKey);
        }

        var output = result.PlainStdOut;
        var certificate = CertificatePathPattern().Match(output) is { Success: true } path
            ? FileSystem.Path.GetFullPath(FileSystem.Path.Combine(result.WorkingDirectory, path.Groups["path"].Value.Trim()))
            : null;
        var password = CertificatePasswordPattern().Match(output) is { Success: true } secret ? secret.Groups["value"].Value : null;
        var tokenUrl = TokenUrlPattern().Match(output) is { Success: true } url && !url.Groups["value"].Value.Contains('<', StringComparison.Ordinal) ? url.Groups["value"].Value : null;
        var tokenScope = TokenScopePattern().Match(output) is { Success: true } scope ? scope.Groups["value"].Value : null;
        var variables = PipelineVariables(output);
        var kind = Text(json["CredentialKind"]) ?? string.Empty;
        var endpoint = variables.TryGetValue(MachineCredentials.Endpoint, out var printed) && !printed.Contains('<', StringComparison.Ordinal)
            ? printed
            : Target.BaseUrl ?? string.Empty;

        return new CreatedMachine
        {
            Id = id,
            AccessKey = accessKey,
            CredentialKind = kind,
            Scopes = (json["Scopes"] as JsonArray)?.Select(item => Text(item) ?? string.Empty).Where(item => item.Length > 0).ToArray() ?? [],
            ProjectKey = projectKey,
            View = view,
            CertificatePath = certificate,
            CertificatePassword = password,
            TokenUrl = tokenUrl,
            TokenScope = tokenScope,
            PrintedVariables = variables,
            Credentials = CredentialsOf(kind, id, accessKey, certificate, password, tokenUrl, tokenScope, endpoint, projectKey, view),
            Result = result,
        };
    }

    private static MachineCredentials? CredentialsOf(string kind, string id, string? accessKey, string? certificate, string? password, string? tokenUrl, string? tokenScope, string endpoint, string projectKey, string view)
    {
        var name = $"machine {id}";
        return kind switch
        {
            "ApiKey" when accessKey is not null => MachineCredentials.ForApiKey(accessKey, endpoint, projectKey, view, name),
            "Certificate" when certificate is not null => MachineCredentials.ForCertificate(certificate, password, endpoint, projectKey),
            "CertificateAndApiKey" when certificate is not null && accessKey is not null => MachineCredentials.ForCertificate(certificate, password, endpoint, projectKey, accessKey),
            "CertificateAndApiKey" when accessKey is not null => MachineCredentials.ForApiKey(accessKey, endpoint, projectKey, view, $"{name} (add the shared certificate)"),
            "ClientCredentials" when accessKey is not null => MachineCredentials.ForClientCredentials(id, accessKey, endpoint, projectKey, tokenUrl, tokenScope),
            _ => null,
        };
    }

    // "  STORYTELLER_X=value" lines after "To sign in sform in a pipeline, set:".
    private static Dictionary<string, string> PipelineVariables(string output)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var start = output.IndexOf("To sign in sform in a pipeline, set:", StringComparison.Ordinal);
        if (start < 0)
        {
            return variables;
        }

        foreach (var line in output[start..].Split('\n').Skip(1))
        {
            var match = PipelineVariablePattern().Match(line);
            if (!match.Success)
            {
                break;
            }

            variables[match.Groups["name"].Value] = match.Groups["value"].Value.TrimEnd();
        }

        return variables;
    }

    private static string? Text(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text) ? text : null;
    }

    [GeneratedRegex(@"""Id""\s*:\s*""(?<id>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex MachineIdPattern();

    [GeneratedRegex(@"Certificate written to (?<path>.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex CertificatePathPattern();

    [GeneratedRegex(@"Certificate password:\s*(?<value>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex CertificatePasswordPattern();

    [GeneratedRegex(@"Token URL:\s*(?<value>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenUrlPattern();

    [GeneratedRegex(@"Token scope:\s*(?<value>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenScopePattern();

    [GeneratedRegex(@"^\s{2}(?<name>STORYTELLER_[A-Z_]+)=(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PipelineVariablePattern();

    // The cleanup of a created machine, as its creator: a delete that fails because the machine is gone (the test deleted
    // it; sform ends the 404 with a failure code) counts as done when account machine no longer lists it.
    private static async Task DeleteMachineAsync(string id, string projectKey, string view, Func<SformRunOptions, string[], Task<SformResult>> run)
    {
        var delete = await run(new SformRunOptions { Label = $"cleanup: delete the machine {id}" }, ["account", "machine", "delete", id, "-p", projectKey, "-v", view]);
        if (delete.Succeeded)
        {
            return;
        }

        var list = await run(new SformRunOptions { Label = $"cleanup: is the machine {id} still there" }, ["account", "machine", "-p", projectKey, "-v", view]);
        if (list.Succeeded && !list.PlainStdOut.Contains(id, StringComparison.Ordinal))
        {
            return;
        }

        throw delete.Fail($"did not delete the machine {id} the test created (account machine {(list.Succeeded ? "still lists it" : $"ended with {SformResult.Describe(list.ExitCode)}")})");
    }
}
