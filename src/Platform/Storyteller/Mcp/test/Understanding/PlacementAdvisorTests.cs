using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.Understanding;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class PlacementAdvisorTests
{
    [Fact]
    public void Currency_OnTheUsage_IsLostToTheSubjectMergedAgainThroughTheContext()
    {
        // inheritance.md "What an ancestor changes": the context carries the subject again and restores its currency
        var effects = EffectsByCandidate(Compositions.Execution(), PlacementTargets.FromPointers(["/currency"]));

        effects["exe.northwind.invoicing.prod"].Effective.ShouldBe(true);
        effects["cnt.northwind.prod"].Effective.ShouldBe(true);
        effects["sbt.northwind"].Effective.ShouldBe(true);

        var usage = effects["usg.northwind.invoicing"];
        usage.Effective.ShouldBe(false);
        usage.WinningStep.ShouldBe(4);
        usage.WinningSourceId.ShouldBe(Compositions.Subject);
        usage.Via.ShouldBe(["sbt.northwind", "cnt.northwind.prod", Compositions.ExecutionKey]);
        effects["rst.invoicing"].Effective.ShouldBe(false);
    }

    [Fact]
    public void Retries_OnAnAncestor_IsOverriddenByTheTemplateOfTheExecution()
    {
        var effects = EffectsByCandidate(Compositions.Execution(), PlacementTargets.FromPointers(["/retries"]));

        effects["exe.northwind.invoicing.prod"].Effective.ShouldBe(true);

        foreach (var ancestor in new[] { "cnt.northwind.prod", "usg.northwind.invoicing", "sbt.northwind", "rst.invoicing" })
        {
            effects[ancestor].Effective.ShouldBe(false, ancestor);
            effects[ancestor].WinningSourceId.ShouldBe(Compositions.Template, ancestor);
            effects[ancestor].WinningStep.ShouldBe(6, ancestor);
        }
    }

    [Fact]
    public void Endpoint_OnTheSubject_IsOverriddenByADescendant()
    {
        var effects = EffectsByCandidate(Compositions.Execution(), PlacementTargets.FromFragment(new JObject { ["endpoint"] = "https://elsewhere.example" }));

        var subject = effects["sbt.northwind"];
        subject.Effective.ShouldBe(false);
        subject.WinningSourceId.ShouldBe(Compositions.Context);
        subject.Via.ShouldBe(["cnt.northwind.prod", Compositions.ExecutionKey]);
    }

    [Fact]
    public void AnEqualValueSetLater_StillWins_OverThePlacedOne()
    {
        // the subject writes NOK too, so the effective value would look right; a marker shows the later step decides
        var effects = EffectsByCandidate(Compositions.Execution(), PlacementTargets.FromFragment(new JObject { ["currency"] = "NOK" }));

        effects["usg.northwind.invoicing"].Effective.ShouldBe(false);
        effects["usg.northwind.invoicing"].WinningStep.ShouldBe(4);
        effects["usg.northwind.invoicing"].Note.ShouldBe(PlacementAdvisor.SameValueNote);
        effects["rst.invoicing"].Effective.ShouldBe(false);
        effects["exe.northwind.invoicing.prod"].Note.ShouldBeNull();
    }

    [Fact]
    public void Arrays_AreEffectiveWhenEveryItemArrives()
    {
        var composition = Compositions.Execution();
        composition.SourceContent(Compositions.Context)["features"] = new JArray("audit");
        composition.Content()["features"] = new JArray("audit");

        var effects = EffectsByCandidate(composition, PlacementTargets.FromFragment(new JObject { ["features"] = new JArray("vat") }));

        // arrays merge by union along the whole sequence: every candidate's item arrives
        effects.Values.ShouldAllBe(effect => effect.Effective == true);
    }

    [Fact]
    public void APointerThroughAnArray_IsNotAssessed()
    {
        // a merge write cannot reach /servers/0: arrays merge by union, so {"servers": [{"host": "b"}]} appends an item
        var composition = Compositions.Execution();
        composition.SourceContent(Compositions.Responsibility)["servers"] = new JArray(new JObject { ["host"] = "a" });
        composition.Content()["servers"] = new JArray(new JObject { ["host"] = "a" });
        composition.AddOrigin("/servers", 1);

        var effects = EffectsByCandidate(composition, PlacementTargets.FromPointers(["/servers/0/host"]));

        effects.Values.ShouldAllBe(effect => effect.Effective == null && effect.Note == PlacementTargets.ThroughArrayNote);
    }

    [Fact]
    public void APointerAtAnObject_IsNotAssessed()
    {
        // the responsibility's database object merges with the execution's: a scalar replay would call it overridden
        var composition = Compositions.Execution();
        composition.SourceContent(Compositions.Responsibility)["database"] = new JObject { ["host"] = "a" };
        composition.SourceContent(Compositions.Own)["database"] = new JObject { ["port"] = 5 };
        composition.Content()["database"] = new JObject { ["host"] = "a", ["port"] = 5 };

        var effects = AllEffectsByCandidate(composition, PlacementTargets.FromPointers(["/database", "/currency"]));

        effects["rst.invoicing"][0].ShouldSatisfyAllConditions(
            effect => effect.Effective.ShouldBeNull(),
            effect => effect.Note.ShouldBe(PlacementTargets.ContainerNote));
        effects["rst.invoicing"][1].Effective.ShouldBe(false);

        // a fragment places the object as given
        var fragment = EffectsByCandidate(composition, PlacementTargets.FromFragment(JObject.Parse("""{"database": {"user": "u"}}""")));
        fragment["rst.invoicing"].Effective.ShouldBe(true);
    }

    [Fact]
    public void Template_WinsOverAncestors_AndIsAddedWhenTheViewHasNone()
    {
        var targets = PlacementTargets.FromPointers(["/plan"]);
        var template = new PlacementCandidate { Kind = PlacementCandidate.TemplateKind, TemplateTypeCode = "exe", Level = "Execution", Meaning = "m", Reason = "r" };

        foreach (var composition in new[] { Compositions.Execution(), Compositions.ExecutionWithoutTemplate() })
        {
            var assessed = PlacementAdvisor.Assess(template, [new PlacementAdvisor.SystemComposition(Compositions.ExecutionKey, composition)], targets);

            assessed.EffectiveForAll.ShouldBe(true);
        }
    }

    [Fact]
    public void Template_LosesToTheAnnotationsOwnContent()
    {
        var composition = Compositions.Execution();
        composition.SourceContent(Compositions.Own)["plan"] = "custom";
        var template = new PlacementCandidate { Kind = PlacementCandidate.TemplateKind, TemplateTypeCode = "exe", Level = "Execution", Meaning = "m", Reason = "r" };

        var assessed = PlacementAdvisor.Assess(template, [new PlacementAdvisor.SystemComposition(Compositions.ExecutionKey, composition)], PlacementTargets.FromPointers(["/plan"]));

        var effect = assessed.Effects.ShouldNotBeNull().ShouldHaveSingleItem();
        effect.Effective.ShouldBe(false);
        effect.WinningSourceId.ShouldBe(Compositions.Own);
        effect.WinningStep.ShouldBe(7);
    }

    [Fact]
    public void ASystemWithoutComposition_IsUnknown_AndDoesNotDecideTheRank()
    {
        var targets = PlacementTargets.FromPointers(["/currency"]);
        var systems = new[]
        {
            new PlacementAdvisor.SystemComposition(Compositions.ExecutionKey, Compositions.Execution()),
            new PlacementAdvisor.SystemComposition("exe.northwind.invoicing.test", null),
        };
        var candidate = new PlacementCandidate { Kind = PlacementCandidate.AnnotationKind, AnnotationKey = "sbt.northwind", Level = "Subject", Meaning = "m", Reason = "r" };

        var assessed = PlacementAdvisor.Assess(candidate, systems, targets);

        assessed.Effects!.Select(effect => effect.Effective).ShouldBe([true, null]);
        assessed.Effects![1].Note.ShouldNotBeNull().ShouldContain("does not exist yet");
        assessed.EffectiveForAll.ShouldBe(true);
    }

    [Fact]
    public void Rank_PutsTheCandidatesEffectiveEverywhereFirst()
    {
        var (candidates, _) = PlacementCandidates.For([AnnotationKey.Parse(Compositions.ExecutionKey)], typeDefault: false);
        var systems = new[] { new PlacementAdvisor.SystemComposition(Compositions.ExecutionKey, Compositions.Execution()) };

        var ranked = PlacementAdvisor.Rank(candidates.Select(candidate => PlacementAdvisor.Assess(candidate, systems, PlacementTargets.FromPointers(["/currency"]))));

        ranked.Select(candidate => candidate.AnnotationKey).ShouldBe(
            ["exe.northwind.invoicing.prod", "cnt.northwind.prod", "sbt.northwind", "usg.northwind.invoicing", "rst.invoicing"]);
    }

    [Fact]
    public void Today_NamesWhereEachValueComesFrom()
    {
        var systems = new[] { new PlacementAdvisor.SystemComposition(Compositions.ExecutionKey, Compositions.Execution()) };

        var today = PlacementAdvisor.Today(systems, PlacementTargets.FromPointers(["/currency", "/database/host"]));

        today[0].ShouldSatisfyAllConditions(
            () => today[0].IsSet.ShouldBeTrue(),
            () => today[0].Value!.Value<string>().ShouldBe("NOK"),
            () => today[0].Step.ShouldBe(4),
            () => today[0].SourceId.ShouldBe(Compositions.Subject),
            () => today[0].Via.ShouldBe(["sbt.northwind", "cnt.northwind.prod", Compositions.ExecutionKey]));
        today[1].IsSet.ShouldBeFalse();
    }

    [Fact]
    public void Targets_FromAFragment_AreItsScalarsAndArrays()
    {
        var targets = PlacementTargets.FromFragment(JObject.Parse("""{"database": {"host": "h", "port": 1, "options": {}}, "tags": ["a"], "gone": null}"""));

        targets.Pointers.ShouldBe(["/database/host", "/database/port", "/tags"]);
        targets.IgnoresNulls.ShouldBeTrue();
        targets.Fragment!["database"]!["host"]!.Value<string>().ShouldBe("h");
        targets.Content["database"]!["host"]!.Value<string>().ShouldNotBe("h");
    }

    [Theory]
    [InlineData("", "the whole document")]
    [InlineData("database", "must be a JSON pointer")]
    [InlineData("/a,/a/b", "name each value once")]
    public void Targets_InvalidPointers_AreInputErrors(string pointers, string message)
    {
        Should.Throw<ToolInputException>(() => PlacementTargets.FromPointers(pointers.Split(','))).Message.ShouldContain(message);
    }

    [Fact]
    public void Targets_AFragmentWithoutValues_IsAnInputError()
    {
        Should.Throw<ToolInputException>(() => PlacementTargets.FromFragment(JObject.Parse("""{"a": null, "b": {}}"""))).Message.ShouldContain("no value to place");
        Should.Throw<ToolInputException>(() => PlacementTargets.FromFragment(JObject.Parse("""{"$remove": ["$.a"]}"""))).Message.ShouldContain("$remove");
    }

    private static Dictionary<string, PlacementEffect> EffectsByCandidate(Sdk.ConfigurationComposition composition, PlacementTargets targets)
    {
        return AllEffectsByCandidate(composition, targets).ToDictionary(pair => pair.Key, pair => pair.Value.ShouldHaveSingleItem());
    }

    private static Dictionary<string, IReadOnlyList<PlacementEffect>> AllEffectsByCandidate(Sdk.ConfigurationComposition composition, PlacementTargets targets)
    {
        var (candidates, _) = PlacementCandidates.For([AnnotationKey.Parse(Compositions.ExecutionKey)], typeDefault: false);
        var systems = new[] { new PlacementAdvisor.SystemComposition(Compositions.ExecutionKey, composition) };

        return candidates.ToDictionary(
            candidate => candidate.AnnotationKey!,
            candidate => PlacementAdvisor.Assess(candidate, systems, targets).Effects!);
    }
}
