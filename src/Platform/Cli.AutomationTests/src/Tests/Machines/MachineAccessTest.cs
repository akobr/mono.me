using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// The steps the cases of Machines and Shared Certificates share, on top of CliTest:
//   - a machine created by a command the test writes itself (the alias set, the context options of MCH-CTX-01, a refusal
//     that must not create one): RunCreatingMachineAsync deletes whatever machine the run printed when the test ends,
//     as CreateMachineAsync does for the machines it creates;
//   - shared certificates: IssueSharedCertificateAsync and RunIssuingSharedCertificateAsync revoke every certificate the
//     test issued when it ends, unless the test revoked it itself (MarkRevoked);
//   - the machine authentication policy of a disposable project (EnableCredentialKindsAsync), the token endpoint a
//     federation rule names as its audience (TokenEndpointOf), the test's platform and its tokens
//     (CreateSignerAsync, SetIssuerAsync, FederatedAsync), the .pfx files sform writes (OpenPfx), and the lines of
//     sform's tables and policies (LineWith, ValueOf).
// Abstract, so the catalogue check takes no test from it: each case is a method of the class of its sub-folder.
public abstract partial class MachineAccessTest : CliTest
{
    // The issuer URL of the catalogue's federation cases (the iss of the test's platform tokens).
    protected const string TestIssuerUrl = "https://issuer.test/";

    // The key set file of the catalogue's federation cases, in the working directory.
    protected const string JwksFile = "jwks.json";

    // The kinds sform knows, as machine-auth prints them.
    protected static readonly IReadOnlyList<string> CredentialKindNames = ["ApiKey", "Certificate", "CertificateAndApiKey", "ClientCredentials", "Federated"];

    // How long a list of trusted issuers may lag behind a change: the API keeps it in a cache of its instance for 60 s
    // (CosmosFederatedIssuerStore) and evicts it only on the instance that made the change.
    protected static readonly TimeSpan IssuerCacheWindow = TimeSpan.FromSeconds(75);

    // The same for the shared certificates of a project: 5 minutes (CosmosSharedCertificateStore).
    protected static readonly TimeSpan SharedCertificateCacheWindow = TimeSpan.FromSeconds(315);

    private readonly HashSet<string> revokedCertificates = new(StringComparer.OrdinalIgnoreCase);
    private int tokenFiles;

    // <Target:BaseUrl>/v1/<organization>/<project>/access/token: the token endpoint of the project, which a federation
    // rule names as its audience and the test's platform tokens carry as their aud (sform builds the same URL from
    // STORYTELLER_ENDPOINT; the server compares the token's aud with the rule's byte for byte).
    protected string TokenEndpointOf(string projectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        var dot = projectKey.IndexOf('.', StringComparison.Ordinal);
        var baseUrl = (Target.BaseUrl ?? throw new InvalidOperationException("Target:BaseUrl is not configured.")).TrimEnd('/');
        return $"{baseUrl}/v1/{projectKey[..dot]}/{projectKey[(dot + 1)..]}/access/token";
    }

    // The machine's variables with STORYTELLER_VIEW (the acting view by default): the variables of a certificate, client
    // credentials or federated machine name no view, and the annotations the cases read are in the acting view.
    protected MachineCredentials InView(MachineCredentials credentials, string? view = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return credentials.With(new Dictionary<string, string?>(StringComparer.Ordinal) { [MachineCredentials.View] = view ?? View });
    }

    // story set <rst> -p <project> -v <view> as the leased user (the acting project and view by default): an annotation the
    // machines of a case read, or the data a view of a context case exists by.
    protected async Task<string> CreateAnnotationAsync(string? projectKey = null, string? view = null)
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(new SformRunOptions { Label = "arrange" }, "story", "set", key, "-p", projectKey ?? ProjectKey, "-v", view ?? View)).ShouldSucceed();
        return key;
    }

    // account machine machine-auth -p <project> -k <kinds> (comma-separated): the policy of a disposable project, the Pre of
    // a P case (a disposable project starts with ApiKey only).
    protected async Task EnableCredentialKindsAsync(string kinds, string? projectKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kinds);
        (await Sform(new SformRunOptions { Label = $"enable the credential kinds {kinds}" }, "account", "machine", "machine-auth", "-p", projectKey ?? ProjectKey, "-k", kinds))
            .ShouldSucceed();
    }

    // Runs a command that creates a machine (account machine create or set) as the test writes it, as the leased user;
    // the machine it printed, if any, is deleted when the test ends (account machine delete <id> -p <projectKey> by the
    // leased user; one that is gone by then counts as deleted). The result is returned as it is, so a refusal is checked
    // by the test.
    protected Task<SformResult> RunCreatingMachineAsync(string projectKey, params string[] arguments)
    {
        return RunCreatingMachineAsync(projectKey, null, new SformRunOptions(), arguments);
    }

    // The same as another pool user (the second user of a P case) or with options; the cleanup still runs as the leased
    // user, the Owner of a disposable project.
    protected async Task<SformResult> RunCreatingMachineAsync(string projectKey, SformActor? actor, SformRunOptions options, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var result = actor is null ? await Sform(options, arguments) : await actor.Sform(options, arguments);
        if (TryMachineIdOf(result) is { } id)
        {
            DeleteMachineAtCleanup(id, projectKey);
        }

        return result;
    }

    // The "Id" of the machine JSON that account machine create, set and reset print; null when the run printed none.
    protected static string? TryMachineIdOf(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return MachineIdPattern().Match(result.PlainStdOut) is { Success: true } match ? match.Groups["id"].Value : null;
    }

    protected static string MachineIdOf(SformResult result)
    {
        return TryMachineIdOf(result) ?? throw result.Fail("printed no machine (no \"Id\")");
    }

    // "Certificate password: <password>" of account machine create or renew (or shared-cert issue).
    protected static string CertificatePasswordOf(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return CertificatePasswordPattern().Match(result.PlainStdOut) is { Success: true } match
            ? match.Groups["value"].Value
            : throw result.Fail("printed no \"Certificate password:\"");
    }

    // The scopes of a machine JSON (create, set, reset).
    protected static IReadOnlyList<string> ScopesOf(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return (result.JsonNode()["Scopes"] as JsonArray)?.Select(scope => scope?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
    }

    // The scope without dots, dashes and case: AnnotationRead, Annotation.Read and annotation-read are one scope.
    protected static string NormalizeScope(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new string(scope.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    // The machine holds every expected scope (it may hold more: the server adds the read scope of a write scope).
    protected static async Task ShouldHaveScopesAsync(IEnumerable<string> scopes, params string[] expected)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var held = scopes.Select(NormalizeScope).ToHashSet(StringComparer.Ordinal);
        foreach (var scope in expected)
        {
            await Assert.That(held).Contains(NormalizeScope(scope));
        }
    }

    // A machine deleted when the test ends, as the leased user: account machine delete <id> -p <project>; a delete that
    // fails because the machine is gone (the test deleted it) counts as done when account machine no longer lists it.
    protected void DeleteMachineAtCleanup(string id, string projectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);

        OnCleanUp(async () =>
        {
            var delete = await Sform(new SformRunOptions { Label = $"cleanup: delete the machine {id}" }, "account", "machine", "delete", id, "-p", projectKey);
            if (delete.Succeeded)
            {
                return;
            }

            var list = await Sform(new SformRunOptions { Label = $"cleanup: is the machine {id} still there" }, "account", "machine", "-p", projectKey);
            if (list.Succeeded && !list.PlainStdOut.Contains(id, StringComparison.Ordinal))
            {
                return;
            }

            throw delete.Fail($"did not delete the machine {id} the test created");
        });
    }

    // account shared-cert issue <label> -o <output> [options] -p <project> (the acting project by default): a shared
    // certificate revoked when the test ends. Throws unless sform succeeds.
    protected Task<IssuedCertificate> IssueSharedCertificateAsync(string label, string output, string? projectKey = null, params string[] options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(options);

        var project = projectKey ?? ProjectKey;
        return RunIssuingSharedCertificateAsync(project, ["account", "shared-cert", "issue", label, "-o", output, .. options, "-p", project]);
    }

    // Runs a command that issues a shared certificate (issue or create) as the test writes it; the certificate it printed
    // is revoked when the test ends (account shared-cert revoke <thumbprint> -p <projectKey>) unless the test revoked it
    // (MarkRevoked). Throws unless sform succeeds.
    protected async Task<IssuedCertificate> RunIssuingSharedCertificateAsync(string projectKey, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentNullException.ThrowIfNull(arguments);

        var result = await Sform(arguments);
        var output = result.PlainStdOut;
        var thumbprint = ThumbprintPattern().Match(output) is { Success: true } printed ? printed.Groups["value"].Value : null;
        if (thumbprint is not null)
        {
            RevokeAtCleanup(thumbprint, projectKey);
        }

        result.ShouldSucceed();
        var label = IssuedLabelPattern().Match(output) is { Success: true } issued ? issued.Groups["value"].Value : throw result.Fail("printed no \"Shared certificate issued:\"");
        var path = CertificatePathPattern().Match(output) is { Success: true } written
            ? FileSystem.Path.GetFullPath(FileSystem.Path.Combine(result.WorkingDirectory, written.Groups["path"].Value.Trim()))
            : throw result.Fail("printed no \"Certificate written to\"");
        return new IssuedCertificate
        {
            Label = label,
            Thumbprint = thumbprint ?? throw result.Fail("printed no \"Thumbprint:\""),
            Path = path,
            Password = CertificatePasswordPattern().Match(output) is { Success: true } password ? password.Groups["value"].Value : null,
            ValidTo = ValidPattern().Match(output) is { Success: true } valid ? valid.Groups["to"].Value : null,
            ProjectKey = projectKey,
            Result = result,
        };
    }

    // The test revoked the certificate itself: the cleanup leaves it.
    protected void MarkRevoked(string thumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);
        revokedCertificates.Add(thumbprint);
    }

    // A machine's variables with a shared certificate added (STORYTELLER_CLIENT_CERTIFICATE and its password): the
    // certificate half of a certificate-and-api-key machine without a certificate of its own.
    protected static MachineCredentials WithSharedCertificate(MachineCredentials credentials, IssuedCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(certificate);
        return credentials.With(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MachineCredentials.ClientCertificate] = certificate.Path,
                [MachineCredentials.ClientCertificatePassword] = certificate.Password,
            },
            certificate.Password is null ? [] : [certificate.Password]);
    }

    // The certificate of a .pfx sform wrote (a path of the working directory or a full one), opened with its password.
    protected X509Certificate2 OpenPfx(string path, string? password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = FileSystem.File.ReadAllBytes(FileSystem.Path.Combine(WorkingDirectory, path));
        return X509CertificateLoader.LoadPkcs12(bytes, password);
    }

    // Whether the working directory (or the full path) has the file.
    protected bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FileSystem.File.Exists(FileSystem.Path.Combine(WorkingDirectory, path));
    }

    // The .pfx opens with the password, holds the private key and expires in about days days (a day either way).
    protected async Task ShouldExpireInDaysAsync(string path, string? password, int days)
    {
        using var certificate = OpenPfx(path, password);
        var left = (certificate.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;
        await Assert.That(certificate.HasPrivateKey).IsTrue();
        await Assert.That(left).IsGreaterThan(days - 1.5);
        await Assert.That(left).IsLessThan(days + 1.5);
    }

    // The test's platform: a new RSA key pair whose key set is written to the working directory (jwks.json by default).
    protected async Task<PlatformTokenSigner> CreateSignerAsync(string jwksFile = JwksFile)
    {
        var signer = new PlatformTokenSigner(CliData.Slug("kid"));
        OnCleanUp(() =>
        {
            signer.Dispose();
            return Task.CompletedTask;
        });
        await WriteFileAsync(jwksFile, signer.Jwks());
        return signer;
    }

    // account machine issuer set <name> --issuer <issuer> --jwks-file jwks.json [options] -p <project>: a trusted issuer
    // of the test's key set, a Pre step of the federation cases.
    protected async Task SetIssuerAsync(string name, string issuer = TestIssuerUrl, string? projectKey = null, params string[] options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentNullException.ThrowIfNull(options);

        (await Sform(
                new SformRunOptions { Label = $"arrange the trusted issuer {name}" },
                ["account", "machine", "issuer", "set", name, "--issuer", issuer, "--jwks-file", JwksFile, .. options, "-p", projectKey ?? ProjectKey]))
            .ShouldSucceed();
    }

    // The variables of a federated machine (its id as STORYTELLER_CLIENT_ID, STORYTELLER_ENDPOINT, STORYTELLER_PROJECT, the
    // acting view) with a platform token the signer signed for the project's token endpoint (or the audience), written to a
    // file of the working directory as STORYTELLER_TOKEN_FILE.
    protected async Task<MachineCredentials> FederatedAsync(
        string machineId,
        PlatformTokenSigner signer,
        string subject,
        IReadOnlyDictionary<string, string>? claims = null,
        TimeSpan? lifetime = null,
        string issuer = TestIssuerUrl,
        string? projectKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
        ArgumentNullException.ThrowIfNull(signer);

        var project = projectKey ?? ProjectKey;
        var token = signer.Sign(issuer, TokenEndpointOf(project), subject, claims, lifetime);
        AddSecret(token);
        var file = await WriteFileAsync($"platform-token-{++tokenFiles}.jwt", token);
        var endpoint = Target.BaseUrl ?? throw new InvalidOperationException("Target:BaseUrl is not configured.");
        return InView(MachineCredentials.ForFederated(machineId, file, endpoint, project));
    }

    // One claim, for FederatedAsync.
    protected static IReadOnlyDictionary<string, string> Claim(string name, string value)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal) { [name] = value };
    }

    // The first line of the output with every part (a row of a table, a line of a policy or an issuer); fails with the
    // output when there is none.
    protected static string LineWith(SformResult result, params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(parts);
        return result.Lines.FirstOrDefault(line => parts.All(part => line.Contains(part, StringComparison.Ordinal)))
            ?? throw result.Fail($"printed no line with {string.Join(" and ", parts.Select(part => $"'{part}'"))}");
    }

    // The cells of the table row whose first cell is firstCell (a machine id, an issuer name, a certificate label): sform's
    // tables pad each cell with a space on both sides, so two spaces or more part the cells and a single space belongs to
    // a cell ("Jwks (stored key set)"). Fails with the output when no row starts with it.
    protected static IReadOnlyList<string> RowOf(SformResult result, string firstCell)
    {
        return TryRowOf(result, firstCell) ?? throw result.Fail($"printed no table row of '{firstCell}'");
    }

    // The same, null when no row starts with it.
    protected static IReadOnlyList<string>? TryRowOf(SformResult result, string firstCell)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstCell);
        return result.Lines
            .Select(line => CellSeparatorPattern().Split(line.Trim()))
            .FirstOrDefault(cells => string.Equals(cells[0], firstCell, StringComparison.Ordinal));
    }

    // The value of the cell of the row whose first cell is firstCell; null without the row or the cell.
    protected static string? CellOf(SformResult result, string firstCell, int index)
    {
        return TryRowOf(result, firstCell) is { } row && index < row.Count ? row[index] : null;
    }

    // Runs sform with the arguments as the leased user until the read passes, at most for within (a run every 5 s), and
    // returns the last result, which the test then checks: a list the API serves from a cache of its instance
    // (IssuerCacheWindow, SharedCertificateCacheWindow) shows a change another instance made only when that cache
    // expires. A read that passes at once returns at once.
    protected async Task<SformResult> EventuallyAsync(Func<SformResult, bool> passes, TimeSpan within, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(arguments);

        var cancellation = TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;
        var deadline = DateTimeOffset.UtcNow + within;
        while (true)
        {
            var result = await Sform(arguments);
            if (passes(result) || DateTimeOffset.UtcNow >= deadline)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
        }
    }

    // The text after "<label>:" on the first line with it, trimmed ("Enabled kinds", "Default kind", "Keys", …).
    protected static string ValueOf(SformResult result, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var line = LineWith(result, label + ":");
        return line[(line.IndexOf(label + ":", StringComparison.Ordinal) + label.Length + 1)..].Trim();
    }

    // The policy machine-auth printed: "Machine authentication policy of <project>:", the enabled kinds (in any order), the
    // default kind and the certificate lifetime (exactly, when given).
    protected static async Task ShouldPrintPolicyAsync(SformResult result, string projectKey, IReadOnlyList<string> enabled, string defaultKind, int? lifetimeDays = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(enabled);

        result.ShouldContain($"Machine authentication policy of {projectKey}:");
        await Assert.That(EnabledKindsOf(result)).IsEquivalentTo(enabled.Order(StringComparer.Ordinal).ToArray());
        await Assert.That(ValueOf(result, "Default kind")).IsEqualTo(defaultKind);
        var lifetime = ValueOf(result, "Certificate lifetime");
        if (lifetimeDays is { } days)
        {
            await Assert.That(lifetime).IsEqualTo($"{days} days");
        }
        else
        {
            await Assert.That(lifetime).EndsWith(" days");
        }
    }

    // The "Enabled kinds:" of a policy, sorted.
    protected static IReadOnlyList<string> EnabledKindsOf(SformResult result)
    {
        return ValueOf(result, "Enabled kinds")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex(@"""Id""\s*:\s*""(?<id>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex MachineIdPattern();

    [GeneratedRegex(@"Certificate password:\s*(?<value>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex CertificatePasswordPattern();

    [GeneratedRegex(@"Certificate written to (?<path>.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex CertificatePathPattern();

    [GeneratedRegex(@"Thumbprint:\s*(?<value>[0-9A-Fa-f]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ThumbprintPattern();

    [GeneratedRegex(@"Shared certificate issued:\s*(?<value>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex IssuedLabelPattern();

    [GeneratedRegex(@"Valid:\s*(?<from>\S+) to (?<to>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ValidPattern();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex CellSeparatorPattern();

    // A shared certificate revoked when the test ends, unless the test revoked it (MarkRevoked).
    private void RevokeAtCleanup(string thumbprint, string projectKey)
    {
        OnCleanUp(async () =>
        {
            if (revokedCertificates.Contains(thumbprint))
            {
                return;
            }

            (await Sform(new SformRunOptions { Label = $"cleanup: revoke the shared certificate {thumbprint}" }, "account", "shared-cert", "revoke", thumbprint, "-p", projectKey))
                .ShouldSucceed();
        });
    }
}
