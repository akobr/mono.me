using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Terminal;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Client Encryption: the recipients of client-encrypted (hyok) values, sform story kv recipients, and the values sform
// encrypts locally for them (kv set --protection hyok), decrypts with a recipient's private key (kv get --private-key
// --kid) and encrypts again for the current recipients (kv reencrypt). The recipients and the level hyok belong to the
// project's policy, so every case runs in a disposable project; the test generates the recipients' RSA keys (3072 bits)
// into its working directory. A write at hyok right after a change of the policy goes through AfterPolicyChangeAsync:
// the API checks it against its instance's cached policy.
[Area(Area.KeyValues)]
public sealed class KeyValueRecipientTests : CliTest
{
    // KV-HYK-01: kv recipients add registers an RSA key from a PEM file, inline, or as JWK, with --id.
    // In a disposable project: kv recipients add --name <a> --public-key a.pem, kv recipients add --name <b> --public-key
    // b.jwk --id <kid-b> and kv recipients add --name <c> --public-key "<inline PEM>" each say "Recipient '<id>' (<name>)
    // added."; kv recipients lists the three, enabled, with RSA keys of 3072 bits, and <kid-b> with the id it was given.
    [Test]
    [TestCase(450)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv recipients")]
    [Covers("story kv recipients add", "--name", "--public-key", "--id")]
    public async Task Recipients_Add_PemJwkInline()
    {
        await CreateDisposableProjectAsync();
        var a = KeyValuesSupportB.NewRecipientKey();
        var b = KeyValuesSupportB.NewRecipientKey();
        var c = KeyValuesSupportB.NewRecipientKey();
        await WriteFileAsync("a.pem", a.PublicPem);
        await WriteFileAsync("b.jwk", b.PublicJwk);
        var (nameA, nameB, nameC) = (CliData.Slug("rcp"), CliData.Slug("rcp"), CliData.Slug("rcp"));
        var kidB = $"kid-b-{CliData.Data.Hex(4)}";

        var addedA = (await Sform(InProject("story", "kv", "recipients", "add", "--name", nameA, "--public-key", "a.pem"))).ShouldSucceed();
        var idA = KeyValuesSupportB.RecipientIdOf(addedA);
        addedA.ShouldContain($"Recipient '{idA}' ({nameA}) added.");

        (await Sform(InProject("story", "kv", "recipients", "add", "--name", nameB, "--public-key", "b.jwk", "--id", kidB)))
            .ShouldSucceed()
            .ShouldContain($"Recipient '{kidB}' ({nameB}) added.");

        var addedC = (await Sform(InProject("story", "kv", "recipients", "add", "--name", nameC, "--public-key", c.PublicPem))).ShouldSucceed();
        var idC = KeyValuesSupportB.RecipientIdOf(addedC);
        addedC.ShouldContain($"Recipient '{idC}' ({nameC}) added.");

        var list = (await Sform(InProject("story", "kv", "recipients"))).ShouldSucceed();
        foreach (var (id, name) in new[] { (idA, nameA), (kidB, nameB), (idC, nameC) })
        {
            var row = list.Lines.SingleOrDefault(line => line.Contains(id, StringComparison.Ordinal) && line.Contains(name, StringComparison.Ordinal))
                ?? throw list.Fail($"listed no recipient '{id}' ({name})");
            await Assert.That(row).Contains("enabled");
            await Assert.That(row).Contains("RSA 3072 bits");
        }

        var recipients = (await PolicyAsync())["Recipients"] as JsonArray;
        await Assert.That(recipients?.Select(recipient => recipient?["Id"]?.GetValue<string>() ?? string.Empty).ToArray()).IsEquivalentTo(new[] { idA, kidB, idC });
    }

    // KV-HYK-02: A key below 3072 bits is refused.
    // In a disposable project, kv recipients add --name <s> --public-key rsa2048.pem ends with 2 and "The public key has
    // 2048 bits; at least 3072 are needed." (Error code: RecipientInvalid), and the project has no recipient.
    [Test]
    [TestCase(451)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv recipients add", "--public-key")]
    public async Task Recipients_SmallKey_Refused()
    {
        await CreateDisposableProjectAsync();
        var small = KeyValuesSupportB.NewRecipientKey(2048);
        await WriteFileAsync("rsa2048.pem", small.PublicPem);

        (await Sform(InProject("story", "kv", "recipients", "add", "--name", CliData.Slug("rcp"), "--public-key", "rsa2048.pem")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("The public key has 2048 bits; at least 3072 are needed.")
            .ShouldContain("Error code: RecipientInvalid");

        (await Sform(InProject("story", "kv", "recipients")))
            .ShouldSucceed()
            .ShouldContain("The project has no recipients; add one with 'recipients add'.");
    }

    // KV-HYK-03: kv set --protection hyok encrypts locally, and get --private-key --kid decrypts.
    // In a disposable project with recipient <a> and hyok allowed (kv policy set --allow public --allow standard --allow
    // hyok), and a public pair and a standard secret beside it: kv set <k> <value> --protection hyok says "Encrypted
    // locally for 1 recipient(s); the platform cannot read the value." and stores a JWE for <a> (kv get --reveal);
    // kv get <k> --private-key a.pem --kid <a> returns the value; kv get <k> without the key shows no value; kv
    // --protection hyok and kv export --protection hyok hold only <k>.
    [Test]
    [TestCase(452)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv set", "--protection", "--protection=hyok")]
    [Covers("story kv get", "--private-key", "--kid")]
    [Covers("story kv", "--protection", "--protection=hyok")]
    [Covers("story kv export", "--protection", "--protection=hyok")]
    public async Task Hyok_SetAndDecrypt()
    {
        await CreateDisposableProjectAsync();
        var a = await AddRecipientAsync("a");
        await AllowHyokAsync();

        var group = CliData.KeyValueKey();
        var key = $"{group}.hyok";
        var plain = $"{group}.plain";
        var standard = $"{group}.standard";
        var value = CliData.SecretValue();
        var standardValue = CliData.SecretValue();
        AddSecret(value);
        AddSecret(standardValue);
        (await Sform(InProject("story", "kv", "set", plain, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = [standardValue] }, InProject("story", "kv", "set", standard, standardValue, "--protection", "standard"))).ShouldSucceed();

        (await AfterPolicyChangeAsync(new SformRunOptions { Secrets = [value] }, InProject("story", "kv", "set", key, value, "--protection", "hyok")))
            .ShouldSucceed()
            .ShouldContain("Encrypted locally for 1 recipient(s); the platform cannot read the value.")
            .ShouldContain($"'{key}' is at version 1 (Hyok).");

        var stored = (await Sform(InProject("story", "kv", "get", key, "--reveal"))).ShouldSucceed().ShouldNotContain(value).JsonNode();
        var document = JsonNode.Parse(stored["Value"]!.GetValue<string>())!;
        await Assert.That((document["recipients"] as JsonArray)?.Select(recipient => recipient?["header"]?["kid"]?.GetValue<string>() ?? string.Empty).ToArray())
            .IsEquivalentTo(new[] { a.Id });

        await AssertDecryptsAsync(key, a, value);

        var hidden = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().ShouldNotContain(value).JsonNode();
        await Assert.That(hidden["Protection"]?.GetValue<string>()).IsEqualTo("Hyok");
        await Assert.That(hidden["Value"]).IsNull();

        (await Sform(InProject("story", "kv", "--protection", "hyok")))
            .ShouldSucceed()
            .ShouldContain(key)
            .ShouldNotContain(plain)
            .ShouldNotContain(standard);

        var exported = KeyValuesSupportB.ItemsOf((await Sform(InProject("story", "kv", "export", "--protection", "hyok"))).ShouldSucceed().ShouldNotContain(value).JsonNode());
        await Assert.That(exported.Keys).IsEquivalentTo(new[] { key });
        await Assert.That(exported[key]["Protection"]?.GetValue<string>()).IsEqualTo("Hyok");
    }

    // KV-HYK-04: recipients disable stops new values for the recipient, and enable resumes them.
    // In a disposable project with recipients <a> and <b> and hyok allowed: kv recipients disable <b> says "Recipient '<b>'
    // is disabled."; kv set <k1> --protection hyok encrypts for 1 recipient, and b's private key cannot decrypt <k1>
    // (exit 2, "The value was not encrypted for the recipient '<b>'."); kv recipients enable <b> says "Recipient '<b>' is
    // enabled."; kv set <k2> --protection hyok encrypts for 2 recipients, and b's key decrypts <k2>.
    [Test]
    [TestCase(453)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv recipients disable", "arg0")]
    [Covers("story kv recipients enable", "arg0")]
    public async Task Recipients_DisableEnable()
    {
        await CreateDisposableProjectAsync();
        await AddRecipientAsync("a");
        var b = await AddRecipientAsync("b");
        await AllowHyokAsync();

        var group = CliData.KeyValueKey();
        var first = $"{group}.first";
        var second = $"{group}.second";
        var firstValue = CliData.SecretValue();
        var secondValue = CliData.SecretValue();
        AddSecret(firstValue);
        AddSecret(secondValue);

        (await Sform(InProject("story", "kv", "recipients", "disable", b.Id)))
            .ShouldSucceed()
            .ShouldContain($"Recipient '{b.Id}' is disabled.");
        (await AfterPolicyChangeAsync(new SformRunOptions { Secrets = [firstValue] }, InProject("story", "kv", "set", first, firstValue, "--protection", "hyok")))
            .ShouldSucceed()
            .ShouldContain("Encrypted locally for 1 recipient(s); the platform cannot read the value.");
        (await Sform(InProject("story", "kv", "get", first, "--private-key", b.PrivateKeyFile, "--kid", b.Id)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The value could not be decrypted with the key for '{b.Id}': The value was not encrypted for the recipient '{b.Id}'.")
            .ShouldNotContain(firstValue);

        (await Sform(InProject("story", "kv", "recipients", "enable", b.Id)))
            .ShouldSucceed()
            .ShouldContain($"Recipient '{b.Id}' is enabled.");
        (await AfterPolicyChangeAsync(new SformRunOptions { Secrets = [secondValue] }, InProject("story", "kv", "set", second, secondValue, "--protection", "hyok")))
            .ShouldSucceed()
            .ShouldContain("Encrypted locally for 2 recipient(s); the platform cannot read the value.");
        await AssertDecryptsAsync(second, b, secondValue);
    }

    // KV-HYK-05: recipients remove -y (and delete) removes a recipient, and a terminal asks first.
    // In a disposable project with recipients <a>, <b>, <c> and <d>, hyok allowed and a hyok value <k> set for all four:
    // kv recipients remove <c> -y and kv recipients delete <b> --yes say "Recipient '<id>' removed."; kv recipients remove
    // <d> in a terminal asks "Remove the recipient '<d>'?" and removes it after Yes; kv recipients then lists only <a>, the
    // enabled recipient that hyok needs, and <k> still decrypts with the removed key of <d>.
    [Test]
    [TestCase(454)]
    [UserRole(Role.Owner)]
    [Terminal]
    [DisposableProject]
    [Covers("story kv recipients remove", "arg0", "-y")]
    [Covers("story kv recipients delete", "arg0", "--yes")]
    public async Task Recipients_Remove()
    {
        await CreateDisposableProjectAsync();
        var a = await AddRecipientAsync("a");
        var b = await AddRecipientAsync("b");
        var c = await AddRecipientAsync("c");
        var d = await AddRecipientAsync("d");
        await AllowHyokAsync();

        var key = CliData.KeyValueKey();
        var value = CliData.SecretValue();
        AddSecret(value);
        (await AfterPolicyChangeAsync(new SformRunOptions { Secrets = [value] }, InProject("story", "kv", "set", key, value, "--protection", "hyok")))
            .ShouldSucceed()
            .ShouldContain("Encrypted locally for 4 recipient(s); the platform cannot read the value.");

        (await Sform(InProject("story", "kv", "recipients", "remove", c.Id, "-y")))
            .ShouldSucceed()
            .ShouldContain($"Recipient '{c.Id}' removed.");
        (await Sform(InProject("story", "kv", "recipients", "delete", b.Id, "--yes")))
            .ShouldSucceed()
            .ShouldContain($"Recipient '{b.Id}' removed.");

        await using (var terminal = await StartSformInTerminal(InProject("story", "kv", "recipients", "remove", d.Id)))
        {
            await terminal.ExpectAsync($"Remove the recipient '{d.Id}'?");
            await terminal.SendAsync("y");
            await terminal.SendKeyAsync(TerminalKey.Enter);
            (await terminal.WaitForExitAsync())
                .ShouldSucceed()
                .ShouldContain($"Recipient '{d.Id}' removed.");
        }

        var list = (await Sform(InProject("story", "kv", "recipients"))).ShouldSucceed();
        list.ShouldContain(a.Id).ShouldNotContain(b.Id).ShouldNotContain(c.Id).ShouldNotContain(d.Id);
        await Assert.That(list.Lines.Single(line => line.Contains(a.Id, StringComparison.Ordinal))).Contains("enabled");

        await AssertDecryptsAsync(key, d, value);
    }

    // KV-HYK-06: kv reencrypt --private-key --kid encrypts values again for the current recipients.
    // In a disposable project with recipient <a>, hyok allowed, and hyok values <k>, <group>.hyok.one and
    // <group>.hyok.two: kv recipients add --name <d> --public-key d.pem; kv reencrypt <k> --private-key a.pem --kid <a>
    // says "1 value(s) encrypted again for 2 recipient(s)."; kv reencrypt --prefix <group>.hyok. --private-key a.pem --kid
    // <a> says "2 value(s) encrypted again for 2 recipient(s)."; d's private key decrypts each of the three.
    [Test]
    [TestCase(455)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv reencrypt", "arg0", "--prefix", "--private-key", "--kid")]
    [Covers("story kv get", "--private-key", "--kid")]
    public async Task Hyok_Reencrypt_ForNewRecipients()
    {
        await CreateDisposableProjectAsync();
        var a = await AddRecipientAsync("a");
        await AllowHyokAsync();

        var group = CliData.KeyValueKey();
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{group}.single"] = CliData.SecretValue(),
            [$"{group}.hyok.one"] = CliData.SecretValue(),
            [$"{group}.hyok.two"] = CliData.SecretValue(),
        };
        foreach (var (key, value) in values)
        {
            AddSecret(value);
            (await AfterPolicyChangeAsync(new SformRunOptions { Secrets = [value] }, InProject("story", "kv", "set", key, value, "--protection", "hyok")))
                .ShouldSucceed()
                .ShouldContain("Encrypted locally for 1 recipient(s); the platform cannot read the value.");
        }

        var d = await AddRecipientAsync("d");

        // each value is encrypted again for <a> and <d>: an instance that has not seen <d> yet refuses it (UnknownRecipient)
        (await AfterPolicyChangeAsync(new SformRunOptions(), InProject("story", "kv", "reencrypt", $"{group}.single", "--private-key", a.PrivateKeyFile, "--kid", a.Id)))
            .ShouldSucceed()
            .ShouldContain("1 value(s) encrypted again for 2 recipient(s).")
            .ShouldNotContain("skipped");
        (await AfterPolicyChangeAsync(new SformRunOptions(), InProject("story", "kv", "reencrypt", "--prefix", $"{group}.hyok.", "--private-key", a.PrivateKeyFile, "--kid", a.Id)))
            .ShouldSucceed()
            .ShouldContain("2 value(s) encrypted again for 2 recipient(s).")
            .ShouldNotContain("skipped");

        foreach (var (key, value) in values)
        {
            await AssertDecryptsAsync(key, d, value);
        }
    }

    // kv recipients add --name <generated> --public-key <letter>.pem with a new RSA-3072 key; the recipient's id (printed)
    // and the file of its private key in the working directory.
    private async Task<Recipient> AddRecipientAsync(string letter)
    {
        var key = KeyValuesSupportB.NewRecipientKey();
        await WriteFileAsync($"{letter}.pem", key.PublicPem);
        var privateKeyFile = $"{letter}.key.pem";
        await WriteFileAsync(privateKeyFile, key.PrivatePem);
        var added = (await Sform(InProject("story", "kv", "recipients", "add", "--name", CliData.Slug("rcp"), "--public-key", $"{letter}.pem"))).ShouldSucceed();
        return new Recipient(KeyValuesSupportB.RecipientIdOf(added), privateKeyFile);
    }

    // A write at hyok (kv set --protection hyok, kv reencrypt) after a change of the policy (hyok allowed, a recipient
    // added, disabled or enabled), run until it succeeds, at most for KeyValuesSupportA.PolicyCacheWait; the last run,
    // which the test checks as usual. The API checks the level and the recipients of the value against its instance's
    // copy of the policy, cached for a minute (a change refreshes only the instance that served it): an older copy
    // refuses the level (409 ProtectionNotAllowed) or a recipient (400 UnknownRecipient), and nothing is written. sform
    // encrypts for the recipients of the stored policy (GET recipients reads past the cache), so a write that passes has
    // the current ones. A refused reencrypt --prefix may have encrypted some values again; the next run does all again.
    private Task<SformResult> AfterPolicyChangeAsync(SformRunOptions options, params string[] arguments)
    {
        return EventuallyAsync(() => Sform(options, arguments), result => result.Succeeded, KeyValuesSupportA.PolicyCacheWait);
    }

    // kv policy set --allow public --allow standard --allow hyok: hyok allowed, which needs an enabled recipient.
    private async Task AllowHyokAsync()
    {
        (await Sform(InProject("story", "kv", "policy", "set", "--allow", "public", "--allow", "standard", "--allow", "hyok")))
            .ShouldSucceed()
            .ShouldContain($"The policy of {ProjectKey} is at version ");
    }

    // kv get <key> --private-key <file> --kid <id> returns the value, decrypted locally.
    private async Task AssertDecryptsAsync(string key, Recipient recipient, string value)
    {
        var pair = (await Sform(new SformRunOptions { Secrets = [value] }, InProject("story", "kv", "get", key, "--private-key", recipient.PrivateKeyFile, "--kid", recipient.Id)))
            .ShouldSucceed()
            .JsonNode();
        await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(value);
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Hyok");
    }

    private async Task<JsonNode> PolicyAsync()
    {
        return (await Sform(InProject("story", "kv", "policy"))).ShouldSucceed().JsonNode();
    }

    // A recipient of the project: its id and the file of its private key (PEM) in the working directory.
    private sealed record Recipient(string Id, string PrivateKeyFile);
}
