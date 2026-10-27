using System.Linq;
using _42.Platform.Cli.Commands.Configuration;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// Which values of a resolved configuration get a mask, compared with the effective document by JSON path.
public class ConfigurationMasksTests
{
    private static readonly JObject Resolved = JObject.Parse("""{ "db": { "host": "db.local", "password": "s3cr3t-value", "port": 5432, "pin": "123", "tls": true, "blank": "  ", "empty": "" }, "added": "only-resolved" }""");
    private static readonly JObject Effective = JObject.Parse("""{ "db": { "host": "db.local", "password": "@(db.password, vault)", "port": "@config(\"/port\")", "pin": "@(db.pin, vault)", "tls": true, "blank": "  ", "empty": "" } }""");

    [Fact]
    public void Plan_UnderBound_MasksWhatResolutionProduced_FromFourCharacters()
    {
        var plan = ConfigurationMasks.Plan(ConfigurationMasks.ReadValues(Resolved), ConfigurationMasks.ByPath(ConfigurationMasks.ReadValues(Effective)), MaskMode.Bound);

        plan.Masked.Select(value => value.Path).ShouldBe(["$.db.password", "$.db.port", "$.added"]);
        plan.TooShort.Select(value => value.Path).ShouldBe(["$.db.pin"]);
        plan.Data.ShouldBe(["s3cr3t-value", "5432", "only-resolved"]);
    }

    [Fact]
    public void Plan_UnderBound_WithoutTheEffectiveDocument_MasksEveryStringAndNumber()
    {
        var plan = ConfigurationMasks.Plan(ConfigurationMasks.ReadValues(Resolved), null, MaskMode.Bound);

        plan.Data.ShouldBe(["db.local", "s3cr3t-value", "5432", "only-resolved"]);
        plan.TooShort.Select(value => value.Path).ShouldBe(["$.db.pin"]);
    }

    [Fact]
    public void Plan_UnderAll_MasksEveryStringAndNumber_ButNoBooleanOrBlankValue()
    {
        var plan = ConfigurationMasks.Plan(ConfigurationMasks.ReadValues(Resolved), null, MaskMode.All);

        plan.Data.ShouldBe(["db.local", "s3cr3t-value", "5432", "123", "only-resolved"]);
        plan.TooShort.ShouldBeEmpty();
    }

    [Fact]
    public void Plan_MasksEveryDistinctValueOnce()
    {
        var plan = ConfigurationMasks.Plan(ConfigurationMasks.ReadValues(JObject.Parse("""{ "a": "same-value", "b": "same-value", "c": "x\nsame-value" }""")), null, MaskMode.All);

        plan.Masked.Count.ShouldBe(3);
        plan.Data.ShouldBe(["same-value", "x\nsame-value", "x"]);
    }

    [Fact]
    public void FindUnresolved_IsAStringWithAtThatTheEffectiveDocumentHolds()
    {
        var resolved = ConfigurationMasks.ReadValues(JObject.Parse("""{ "a": "@(db.password, vault)", "b": "@handle", "c": "@config(\"/x\")", "d": "plain" }"""));
        var effective = ConfigurationMasks.ByPath(ConfigurationMasks.ReadValues(JObject.Parse("""{ "a": "@(db.password, vault)", "b": "@other", "d": "plain" }""")));

        ConfigurationMasks.FindUnresolved(resolved, effective).Select(value => value.Path).ShouldBe(["$.a"]);
    }

    [Fact]
    public void ReadValues_OfAnEffectiveDocumentThatBreaksTheRules_ReadsEveryValue()
    {
        var values = ConfigurationMasks.ReadValues(JObject.Parse("""{ "a:b": 1, "A": { "$binding": "@x" }, "a": "y" }"""));

        values.Select(value => value.Path).ShouldBe(["$['a:b']", "$.A['$binding']", "$.a"]);
        ConfigurationMasks.ReadValues(null).ShouldBeEmpty();
    }

    [Fact]
    public void ToString_OfAPlan_NamesNoValue()
    {
        var plan = ConfigurationMasks.Plan(ConfigurationMasks.ReadValues(Resolved), null, MaskMode.All);

        plan.ToString().ShouldBe("5 masked, 0 too short, 5 masks");
    }
}
