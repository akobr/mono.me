using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Pairs: sform story kv set and get of public pairs, the value sources and the versions.
[Area(Area.KeyValues)]
public sealed class KeyValuePairTests : CliTest
{
    // KV-PAR-01: kv set with --protection public and kv get round-trip a public pair.
    // As an Editor, story kv set <key> <value> --protection public says the pair is at version 1 (Public), and
    // story kv get <key> returns it with the value, the level Public and version 1.
    [Test]
    [TestCase(349)]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("story kv set", "arg0", "arg1", "--protection", "--protection=public")]
    [Covers("story kv get", "arg0")]
    public async Task Kv_SetGetPublic_RoundTrip()
    {
        var key = CliData.KeyValueKey();
        var value = CliData.Value();

        (await Sform(InProject("story", "kv", "set", key, value, "--protection", "public")))
            .ShouldSucceed()
            .ShouldContain($"'{key}' is at version 1 (Public).");

        var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(pair["Key"]?.GetValue<string>()).IsEqualTo(key);
        await Assert.That(pair["Value"]?.GetValue<string>()).IsEqualTo(value);
        await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Public");
        await Assert.That(pair["Version"]?.GetValue<int>()).IsEqualTo(1);
    }
}
