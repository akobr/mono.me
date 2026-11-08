using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Secrets: secret pairs at the project's levels, revealed only to ContributorWithSecrets and up.
[Area(Area.KeyValues)]
public sealed class KeyValueSecretTests : CliTest
{
    // KV-SEC-01: --secret stores a secret at the project's default level, and get hides its value.
    // As a ContributorWithSecrets, story kv set <key> <secret> --secret stores it at the project's default secret level
    // (not Public); story kv get <key> returns the pair without its value, and story kv --prefix <key> lists the key; the
    // secret's value is in none of the runs' outputs.
    [Test]
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
}
