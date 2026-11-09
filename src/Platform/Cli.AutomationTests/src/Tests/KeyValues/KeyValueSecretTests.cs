using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Secrets: secret pairs at the project's levels, revealed only to ContributorWithSecrets and up.
[Area(Area.KeyValues)]
public sealed class KeyValueSecretTests : CliTest
{
    // What sform prints after "Error code: MtlsRequired" (KeyValueConsoleExtensions.MtlsRequiredHint of the CLI).
    private const string MtlsRequiredHint = "This project delivers secrets to machines only over mTLS: call with a machine certificate, or sign in as a user with the ContributorWithSecrets role.";

    // KV-SEC-01: --secret stores a secret at the project's default level, and get hides its value.
    // As a ContributorWithSecrets, story kv set <key> <secret> --secret stores it at the project's default secret level
    // (not Public); story kv get <key> returns the pair without its value, and story kv --prefix <key> lists the key; the
    // secret's value is in none of the runs' outputs.
    [Test]
    [TestCase(380)]
    [Elementary]
    [UserRole(Role.Secrets)]
    [Covers("story kv set", "--secret")]
    public async Task Kv_Secret_HiddenByDefault()
    {
        var key = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);

        var set = (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", key, secret, "--secret"))).ShouldSucceed();
        set.ShouldContain($"'{key}' is at version 1 (").ShouldNotContain("(Public)").ShouldNotContain(secret);

        var get = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().ShouldNotContain(secret);
        var pair = get.JsonNode();
        await Assert.That(pair["Key"]?.GetValue<string>()).IsEqualTo(key);
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsNotEqualTo("Public");
        await Assert.That(pair["Value"]?.GetValue<string>()).IsNull();

        (await Sform(InProject("story", "kv", "--prefix", key)))
            .ShouldSucceed()
            .ShouldContain(key)
            .ShouldNotContain(secret);
    }

    // KV-SEC-02: get --reveal returns the secret's value to ContributorWithSecrets.
    // As a ContributorWithSecrets, a secret set with --secret; story kv get <key> --reveal returns the pair with the
    // secret's value, and story kv get <key> --reveal --value-only prints the value alone.
    [Test]
    [TestCase(381)]
    [UserRole(Role.Secrets)]
    [Covers("story kv get", "--reveal")]
    public async Task Kv_Reveal_ReturnsValue()
    {
        var key = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", key, secret, "--secret"))).ShouldSucceed();

        var revealed = (await Sform(InProject("story", "kv", "get", key, "--reveal"))).ShouldSucceed().JsonNode();
        await Assert.That(revealed["Key"]?.GetValue<string>()).IsEqualTo(key);
        await Assert.That(revealed["Value"]?.GetValue<string>()).IsEqualTo(secret);

        var alone = (await Sform(InProject("story", "kv", "get", key, "--reveal", "--value-only"))).ShouldSucceed();
        await Assert.That(alone.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray()).IsEquivalentTo(new[] { secret });
    }

    // KV-SEC-03: A Contributor can neither set nor reveal a secret.
    // A ContributorWithSecrets sets <secret> in the acting project; as a Contributor, story kv set <key> x --secret ends
    // with exit 44, and story kv get <secret> --reveal ends with exit 44 without the value.
    [Test]
    [TestCase(382)]
    [UserRole(Role.Contributor)]
    [Covers("story kv set", "--secret")]
    [Covers("story kv get", "--reveal")]
    public async Task Kv_SecretAsContributor_Refused()
    {
        var holder = await ArrangeAsAsync(SecretsUser);
        var secretKey = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await holder.Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", secretKey, secret, "--protection", "standard"))).ShouldSucceed();

        var key = CliData.KeyValueKey();
        var value = CliData.SecretValue();
        AddSecret(value);
        (await Sform(new SformRunOptions { Secrets = [value] }, InProject("story", "kv", "set", key, value, "--secret")))
            .ShouldExitWith(CliExit.Unauthorized);

        (await Sform(InProject("story", "kv", "get", secretKey, "--reveal")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldNotContain(secret);
    }

    // KV-SEC-04: A secret without a value source asks with hidden input in a terminal, and fails fast on redirected input.
    // As a ContributorWithSecrets, story kv set <key> --secret in a pseudo terminal asks "Value of '<key>'"; the typed
    // value is not echoed and is stored (kv get --reveal --value-only). story kv set <key2> --secret with the input
    // redirected and no value ends with exit 2 at once, with "! sform cannot ask for the value of '<key2>': the input is
    // redirected." and "Pass it with --stdin, with -i|--import <file>, or as an argument." on stderr.
    [Test]
    [TestCase(383)]
    [UserRole(Role.Secrets)]
    [Terminal]
    [Covers("story kv set")]
    public async Task Kv_SecretPrompt_HiddenInput()
    {
        var key = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);

        await using (var terminal = await StartSformInTerminal(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", key, "--secret")))
        {
            await terminal.ExpectAsync($"Value of '{key}'");
            await terminal.SendLineAsync(secret);
            (await terminal.WaitForExitAsync())
                .ShouldSucceed()
                .ShouldContain($"'{key}' is at version 1 (")
                .ShouldNotContain(secret);
        }

        var stored = (await Sform(InProject("story", "kv", "get", key, "--reveal", "--value-only"))).ShouldSucceed();
        await Assert.That(stored.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray()).IsEquivalentTo(new[] { secret });

        var secondKey = CliData.KeyValueKey();
        (await Sform(InProject("story", "kv", "set", secondKey, "--secret")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"! sform cannot ask for the value of '{secondKey}': the input is redirected.", SformStream.StdErr)
            .ShouldContain("Pass it with --stdin, with -i|--import <file>, or as an argument.", SformStream.StdErr);
        (await Sform(InProject("story", "kv", "get", secondKey))).ShouldExitWith(CliExit.WrongInput);
    }

    // KV-SEC-05: --protection standard stores a server-side secret at that level.
    // As a ContributorWithSecrets, story kv set <k1> <secret> --protection standard says "'<k1>' is at version 1
    // (Standard)."; kv get shows the level Standard without the value, and the list (kv --prefix <k1> --values) shows the
    // key at Standard with its value masked.
    [Test]
    [TestCase(384)]
    [UserRole(Role.Secrets)]
    [Covers("story kv set", "--protection", "--protection=standard")]
    public async Task Kv_StandardLevel_Stored()
    {
        var key = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);

        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", key, secret, "--protection", "standard")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 1 (Standard).");

        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().ShouldNotContain(secret).JsonNode();
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Standard");
        await Assert.That(pair["Value"]?.GetValue<string>()).IsNull();

        var list = (await Sform(InProject("story", "kv", "--prefix", key, "--values"))).ShouldSucceed().ShouldNotContain(secret);
        await Assert.That(KeyValuesSupportA.RowOf(list, key)).Contains("Standard");
    }

    // KV-SEC-06: A machine reads secrets only with Secrets.Read, over mTLS when the policy requires it.
    // In a disposable project the Owner enables the ApiKey and Certificate machines (account machine machine-auth -k
    // ApiKey,Certificate), turns the mTLS rule off (kv policy set --require-mtls false), sets <secret> and creates three
    // machines: an API key machine with -r --scope SecretsRead, one with -r only, and a certificate machine (-k
    // Certificate) with -r --scope SecretsRead (a machine reads a pair with ConfigurationRead, and SecretsRead implies no
    // Configuration scope). story kv get <secret> --reveal: the -r machine is refused (exit 44); the SecretsRead API key
    // machine gets the value. After kv policy set --require-mtls true the API key machine is refused with exit 44,
    // "Error code: MtlsRequired" and the hint, and the certificate machine gets the value over mTLS. The API caches the
    // policy for a minute per instance, so the reveals that depend on a change are repeated until it applies.
    [Test]
    [TestCase(385)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv get", "--reveal")]
    public async Task Kv_MachineSecrets_ScopeAndMtls()
    {
        await CreateDisposableProjectAsync();
        (await Sform("account", "machine", "machine-auth", "-p", ProjectKey, "-k", "ApiKey,Certificate")).ShouldSucceed();
        (await Sform(InProject("story", "kv", "policy", "set", "--require-mtls", "false"))).ShouldSucceed();

        var key = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", key, secret, "--secret"))).ShouldSucceed();

        var secretsReader = await CreateMachineAsync("-k", "ApiKey", "-r", "--scope", "SecretsRead");
        var reader = await CreateMachineAsync("-k", "ApiKey", "-r");
        var certificate = await CreateMachineAsync("-k", "Certificate", "-r", "--scope", "SecretsRead");
        string[] reveal = ["story", "kv", "get", key, "--reveal"];

        (await Sform(AsMachine(reader.ApiKeyCredentials), reveal))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldNotContain(secret);

        var withoutMtls = await EventuallyAsync(() => Sform(AsMachine(secretsReader.ApiKeyCredentials), reveal), result => result.Succeeded, KeyValuesSupportA.PolicyCacheWait);
        await Assert.That(withoutMtls.ShouldSucceed().JsonNode()["Value"]?.GetValue<string>()).IsEqualTo(secret);

        (await Sform(InProject("story", "kv", "policy", "set", "--require-mtls", "true"))).ShouldSucceed();

        var withMtls = await EventuallyAsync(() => Sform(AsMachine(secretsReader.ApiKeyCredentials), reveal), result => !result.Succeeded, KeyValuesSupportA.PolicyCacheWait);
        withMtls
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain("Error code: MtlsRequired")
            .ShouldContain(MtlsRequiredHint)
            .ShouldNotContain(secret);

        var overMtls = (await Sform(AsMachine(certificate.SignInCredentials), reveal)).ShouldSucceed();
        await Assert.That(overMtls.JsonNode()["Value"]?.GetValue<string>()).IsEqualTo(secret);
    }
}
