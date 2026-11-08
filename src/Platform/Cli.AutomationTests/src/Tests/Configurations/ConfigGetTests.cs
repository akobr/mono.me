using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// Get and Formats: sform story config reads a configuration as its model, resolved, or as content in a format.
[Area(Area.Configurations)]
public sealed class ConfigGetTests : CliTest
{
    // CFG-GET-01: story config (and configuration) returns the configuration model.
    // An Editor creates <rst> with a configuration (story set, config set -x); as a Reader, story config <rst> prints the
    // model with the content and its version, and story configuration <rst> (the alias) the same content.
    [Test]
    [TestCase(368)]
    [Elementary]
    [UserRole(Role.Reader)]
    [Covers("story config", "arg0")]
    [Covers("story configuration", "arg0")]
    public async Task Config_Get_ReturnsModel()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        var probe = CliData.Value();
        (await editor.Sform(InProject("story", "set", key))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", key, "-x", $"probe={probe}", "-x", "count=3"))).ShouldSucceed();

        var model = (await Sform(InProject("story", "config", key))).ShouldSucceed().JsonNode();
        await Assert.That(model["Content"]?["probe"]?.GetValue<string>()).IsEqualTo(probe);
        await Assert.That(model["Content"]?["count"]?.GetValue<long>()).IsEqualTo(3L);
        await Assert.That(model["Version"]?.GetValue<int>() ?? 0).IsGreaterThanOrEqualTo(1);

        var alias = (await Sform(InProject("story", "configuration", key))).ShouldSucceed().JsonNode();
        await Assert.That(alias["Content"]?["probe"]?.GetValue<string>()).IsEqualTo(probe);
        await Assert.That(alias["Version"]?.GetValue<int>()).IsEqualTo(model["Version"]!.GetValue<int>());
    }

    // CFG-GET-02: -r returns the resolved configuration with bindings and inheritance applied.
    // An Editor creates a public pair, a parent configuration on <rst> and one on its unit <rst>.<unt> that binds the
    // pair (@<key>); as a Reader, story config <unt> -r (and --resolved) has the parent's value merged in and the pair's
    // value in place of the binding, while the plain read keeps the binding expression.
    [Test]
    [TestCase(369)]
    [Elementary]
    [UserRole(Role.Reader)]
    [Covers("story config", "-r", "--resolved")]
    public async Task Config_Resolved_AppliesBindings()
    {
        var editor = await ArrangeAsEditorAsync();
        var parent = CliData.ResponsibilityKey();
        var unit = CliData.UnitKeyOf(parent);
        var pair = CliData.KeyValueKey();
        var pairValue = CliData.Value();
        var inherited = CliData.Value();
        var own = CliData.Value();
        (await editor.Sform(InProject("story", "set", parent))).ShouldSucceed();
        (await editor.Sform(InProject("story", "set", unit))).ShouldSucceed();
        (await editor.Sform(InProject("story", "kv", "set", pair, pairValue, "--protection", "public"))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", parent, "-x", $"inherited={inherited}"))).ShouldSucceed();
        (await editor.Sform(InProject("story", "config", "set", unit, "-x", $"own={own}", "-x", $"bound=@{pair}"))).ShouldSucceed();

        foreach (var option in new[] { "-r", "--resolved" })
        {
            var resolved = (await Sform(InProject("story", "config", unit, option))).ShouldSucceed().JsonNode()["Content"];
            await Assert.That(resolved?["inherited"]?.GetValue<string>()).IsEqualTo(inherited);
            await Assert.That(resolved?["own"]?.GetValue<string>()).IsEqualTo(own);
            await Assert.That(resolved?["bound"]?.GetValue<string>()).IsEqualTo(pairValue);
        }

        var effective = (await Sform(InProject("story", "config", unit))).ShouldSucceed().JsonNode()["Content"];
        await Assert.That(effective?["bound"]?.GetValue<string>()).IsEqualTo($"@{pair}");
        await Assert.That(effective?["inherited"]?.GetValue<string>()).IsEqualTo(inherited);
    }
}
