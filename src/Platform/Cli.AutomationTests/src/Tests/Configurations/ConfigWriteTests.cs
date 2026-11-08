using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// Set, Patch and Delete: sform story config set, patch and delete.
[Area(Area.Configurations)]
public sealed class ConfigWriteTests : CliTest
{
    // CFG-SET-01: config set -x merges inline properties into the stored content.
    // As an Editor, config set <rst> -x a=1 -x b.c=true stores a=1 (a number) and "b.c"=true (a boolean; -x names a
    // property, it does not nest); config set <rst> --properties d=x merges d="x" into it as a new version, keeping a
    // and b.c, and story config <rst> --content-only returns the merged content.
    [Test]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("story config set", "arg0", "-x", "--properties")]
    public async Task Config_SetProperties_Merges()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();

        var first = (await Sform(InProject("story", "config", "set", key, "-x", "a=1", "-x", "b.c=true"))).ShouldSucceed().JsonNode();
        await Assert.That(first["Content"]?["a"]?.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(first["Content"]?["b.c"]?.GetValue<bool>()).IsTrue();
        var firstVersion = first["Version"]!.GetValue<int>();

        var second = (await Sform(InProject("story", "config", "set", key, "--properties", "d=x"))).ShouldSucceed().JsonNode();
        await Assert.That(second["Version"]!.GetValue<int>()).IsGreaterThan(firstVersion);

        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(content["a"]?.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(content["b.c"]?.GetValue<bool>()).IsTrue();
        await Assert.That(content["d"]?.GetValue<string>()).IsEqualTo("x");
    }
}
