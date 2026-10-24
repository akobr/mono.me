using _42.Platform.Storyteller.Mcp.Understanding;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class ConfigurationExplainerTests
{
    private static readonly Dictionary<string, KeyValueMetadata> NoPairs = new();

    [Fact]
    public void Value_NamesItsStepSourceNodeAndBranch()
    {
        var explanation = Explain(Compositions.Execution());

        var currency = explanation.Values.Single(value => value.Pointer == "/currency");
        currency.Value!.Value<string>().ShouldBe("NOK");
        currency.Step.ShouldBe(4);
        currency.Source!.Id.ShouldBe(Compositions.Subject);
        currency.Source.AnnotationKey.ShouldBe("sbt.northwind");
        currency.Node.ShouldBe(new ExplainedValue.ValueNode("sbt.northwind", 2));
        currency.Via.ShouldBe(["sbt.northwind", "cnt.northwind.prod", Compositions.ExecutionKey]);
    }

    [Fact]
    public void AlsoSetBy_ListsTheOverriddenSteps_BeforeTheWinner()
    {
        var explanation = Explain(Compositions.Execution());

        // the currency comes back from the subject through the context, over the usage's USD
        var setters = explanation.Values.Single(value => value.Pointer == "/currency").AlsoSetBy!;
        setters.Select(setter => setter.Step).ShouldBe([2, 3]);
        setters[0].Value!.Value<string>().ShouldBe("NOK");
        setters[0].Via.ShouldBe(["sbt.northwind", "usg.northwind.invoicing", Compositions.ExecutionKey]);
        setters[1].SourceId.ShouldBe(Compositions.Usage);
        setters[1].Value!.Value<string>().ShouldBe("USD");
        setters.ShouldAllBe(setter => setter.BeforeWinner);
    }

    [Fact]
    public void AlsoSetBy_ASetterAfterTheWinner_IsNotBeforeIt()
    {
        var explanation = Explain(Compositions.Execution());

        // the residency keeps its first application; the second, through the context, repeats the same value
        var residency = explanation.Values.Single(value => value.Pointer == "/residency");
        residency.Step.ShouldBe(2);
        var setter = residency.AlsoSetBy.ShouldHaveSingleItem();
        setter.Step.ShouldBe(4);
        setter.BeforeWinner.ShouldBeFalse();
    }

    [Fact]
    public void Template_IsNamedByItsTypeCode()
    {
        var explanation = Explain(Compositions.Execution());

        var retries = explanation.Values.Single(value => value.Pointer == "/retries");
        retries.Source!.Kind.ShouldBe(Sdk.ConfigurationCompositionSourceKind.Template);
        retries.Source.TypeCode.ShouldBe("exe");
        retries.Source.AnnotationKey.ShouldBeNull();
        retries.AlsoSetBy.ShouldBeNull();
    }

    [Fact]
    public void Sources_CountTheirValues_AndListMissingOnes()
    {
        var explanation = Explain(Compositions.Execution());

        var counts = explanation.Sources.ToDictionary(source => source.Id, source => source.Values);
        counts[Compositions.Subject].ShouldBe(2);
        counts[Compositions.Usage].ShouldBe(1);
        counts[Compositions.Context].ShouldBe(1);
        counts[Compositions.Template].ShouldBe(1);
        counts[Compositions.Responsibility].ShouldBe(0);
        counts[Compositions.Own].ShouldBe(0);
        explanation.Sources.Single(source => source.Id == Compositions.Own).Presence.ShouldBe(Sdk.ConfigurationCompositionSourcePresence.Missing);
    }

    [Fact]
    public void Pointer_ExplainsOnlyTheValuesAtAndUnderIt()
    {
        var composition = WithAddress(Compositions.Execution());

        var explanation = Explain(composition, pointer: "/address");

        explanation.Values.Select(value => value.Pointer).ShouldBe(["/address", "/address/city", "/address/zip"]);
        explanation.Values[0].Value.ShouldBeNull();
        explanation.Values[0].ValueKind.ShouldBe("object");
        explanation.Pointer.ShouldBe("/address");
        explanation.Sources.Single(source => source.Id == Compositions.Subject).Values.ShouldBe(0);
    }

    [Fact]
    public void NestedMember_ListsTheOtherDocumentsAtThatPath()
    {
        var composition = WithAddress(Compositions.Execution());

        var explanation = Explain(composition, pointer: "/address/city");

        var city = explanation.Values.ShouldHaveSingleItem();
        city.Step.ShouldBe(5);
        var setter = city.AlsoSetBy.ShouldHaveSingleItem();
        setter.SourceId.ShouldBe(Compositions.Usage);
        setter.Value!.Value<string>().ShouldBe("Oslo");
        setter.BeforeWinner.ShouldBeTrue();
    }

    [Fact]
    public void ArrayItems_AreAttributedByTheirOriginsOnly()
    {
        var composition = Compositions.Execution();
        composition.SourceContent(Compositions.Subject)["features"] = new JArray("vat");
        composition.SourceContent(Compositions.Usage)["features"] = new JArray("e-invoice");
        composition.Content()["features"] = new JArray("vat", "e-invoice");
        composition.AddOrigin("/features", 2);
        composition.AddOrigin("/features/0", 2);
        composition.AddOrigin("/features/1", 3);

        var explanation = Explain(composition, pointer: "/features");

        var features = explanation.Values[0];
        features.ValueKind.ShouldBe("array");
        features.AlsoSetBy!.Select(setter => (setter.Step, setter.ValueKind, setter.BeforeWinner)).ShouldBe([(3, "array", false), (4, "array", false)]);
        explanation.Values[1].Value!.Value<string>().ShouldBe("vat");
        explanation.Values[1].AlsoSetBy.ShouldBeNull();
        explanation.Values[2].Step.ShouldBe(3);
        explanation.Values[2].AlsoSetBy.ShouldBeNull();
    }

    [Fact]
    public void Values_AreCappedAt200()
    {
        var composition = Compositions.Execution();

        for (var index = 0; index < 250; index++)
        {
            composition.Content()[$"k{index}"] = index;
            composition.AddOrigin($"/k{index}", 5);
        }

        var explanation = Explain(composition);

        explanation.Values.Count.ShouldBe(ConfigurationExplainer.MaxValues);
        explanation.IsTruncated.ShouldBeTrue();
        explanation.Sources.Single(source => source.Id == Compositions.Context).Values.ShouldBe(251);
    }

    [Fact]
    public void Bindings_AreParsed_WithTheMetadataOfTheirPairs()
    {
        var composition = WithBindings(Compositions.Execution());
        var pairs = new Dictionary<string, KeyValueMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["db.password"] = new() { Exists = true, IsSecret = true, IsDeleted = false, Protection = Sdk.KeyValueProtection.Standard, Version = 3 },
            ["db.host"] = KeyValueMetadata.Missing,
            ["public.only"] = new() { Exists = true, IsSecret = true, IsDeleted = false, Protection = Sdk.KeyValueProtection.Hsm, Version = 1 },
        };

        var explanation = ConfigurationExplainer.Explain(composition, composition.Hash, string.Empty, pairs, ConfigurationExplainer.ConsumersUnavailable("not asked"));

        var bindings = explanation.Bindings.ToDictionary(binding => binding.Pointer);
        var password = bindings["/password"];
        password.Form.ShouldBe(BindingForm.SourceCall);
        password.Expression.ShouldBe("@secret(db.password)");
        var reference = password.References.ShouldHaveSingleItem();
        (reference.Key, reference.Source, reference.IsValuesStore).ShouldBe(("db.password", "secret", true));
        reference.KeyValue!.Version.ShouldBe(3);
        reference.Note.ShouldContain("may read secrets");

        bindings["/host"].References.ShouldHaveSingleItem().Note.ShouldContain("No such pair");
        bindings["/declined"].References.ShouldHaveSingleItem().Note.ShouldContain("public source declines a secret");
        bindings["/vault"].References.ShouldHaveSingleItem().IsValuesStore.ShouldBeFalse();
        bindings["/url"].Form.ShouldBe(BindingForm.Interpolation);
        bindings["/url"].References.ShouldHaveSingleItem().Note.ShouldContain("Not looked up");

        var rule = bindings["/rule"];
        rule.Form.ShouldBe(BindingForm.Object);
        rule.Name.ShouldBe("jlogic");
        rule.IsValid.ShouldBeTrue();
        var context = bindings["/rule/$context/tier"];
        context.EnvelopePointer.ShouldBe("/rule");
        context.Reads.ShouldBe(["this document (@config)"]);
        // the program of an object binding is not bound
        bindings.Keys.ShouldNotContain(pointer => pointer.StartsWith("/rule/$definition", StringComparison.Ordinal));
    }

    [Fact]
    public void ValuesStoreKeys_AreReadOnce_WithoutCase()
    {
        var composition = WithBindings(Compositions.Execution());
        composition.Content()["again"] = "@DB.Password";

        var keys = ConfigurationExplainer.ValuesStoreKeys(ConfigurationExplainer.BindingsOf(composition, string.Empty));

        keys.ShouldBe(["db.password", "db.host", "public.only", "host.value"]);
    }

    [Fact]
    public void Consumers_AreGroupedByTheirRestriction()
    {
        var machines = new[]
        {
            Machine("whole-project", null, Sdk.MachineScope.ConfigurationRead),
            Machine("subject", "sbt.northwind", Sdk.MachineScope.ConfigurationRead, Sdk.MachineScope.SecretsRead),
            Machine("itself", Compositions.ExecutionKey, Sdk.MachineScope.ConfigurationWrite),
            Machine("unit-job", "uxe.northwind.invoicing.prod.export", Sdk.MachineScope.ConfigurationRead),
            Machine("annotations-only", null, Sdk.MachineScope.AnnotationRead),
            Machine("other-customer", "sbt.contoso", Sdk.MachineScope.ConfigurationRead),
        };

        var consumers = ConfigurationExplainer.Consumers(AnnotationKey.Parse(Compositions.ExecutionKey), machines);

        consumers.Note.ShouldBe(ConfigurationConsumers.RuntimeNote);
        consumers.Machines!.ReadsThisDocument.Select(machine => machine.Id).ShouldBe(["whole-project", "subject", "itself"]);
        consumers.Machines.ReadsThisDocument[1].SecretsRead.ShouldBeTrue();
        consumers.Machines.ReadsThisDocument[0].SecretsRead.ShouldBeFalse();
        consumers.Machines.ReadsThroughDescendants.ShouldHaveSingleItem().Id.ShouldBe("unit-job");
    }

    [Theory]
    [InlineData("c0ffee12", false)]
    [InlineData("deadbeef", true)]
    public void IsStale_ComparesTheHashes(string effectiveHash, bool stale)
    {
        var composition = Compositions.Execution();

        var explanation = ConfigurationExplainer.Explain(composition, effectiveHash, string.Empty, NoPairs, ConfigurationExplainer.ConsumersUnavailable("not asked"));

        explanation.IsStale.ShouldBe(stale);
        explanation.EffectiveHash.ShouldBe(effectiveHash);
        (explanation.Notes[0] == ConfigurationExplainer.StaleNote).ShouldBe(stale);
        explanation.Notes.ShouldContain(note => note.Contains("not evaluated"));
    }

    private static ConfigurationExplanation Explain(Sdk.ConfigurationComposition composition, string pointer = "")
    {
        return ConfigurationExplainer.Explain(composition, composition.Hash, pointer, NoPairs, ConfigurationExplainer.ConsumersUnavailable("not asked"));
    }

    // inheritance.md "Nested objects": the usage sets the address, the context changes the city.
    private static Sdk.ConfigurationComposition WithAddress(Sdk.ConfigurationComposition composition)
    {
        composition.SourceContent(Compositions.Usage)["address"] = new JObject { ["city"] = "Oslo", ["zip"] = "0001" };
        composition.SourceContent(Compositions.Context)["address"] = new JObject { ["city"] = "Bergen" };
        composition.Content()["address"] = new JObject { ["city"] = "Bergen", ["zip"] = "0001" };
        composition.AddOrigin("/address", 3);
        composition.AddOrigin("/address/city", 5);
        composition.AddOrigin("/address/zip", 3);
        return composition;
    }

    private static Sdk.ConfigurationComposition WithBindings(Sdk.ConfigurationComposition composition)
    {
        var bindings = new JObject
        {
            ["password"] = "@secret(db.password)",
            ["host"] = "@db.host",
            ["declined"] = "@public(public.only)",
            ["vault"] = "@(db.password, primaryVault)",
            ["url"] = "@[https://@host.value/api]",
            ["rule"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = new JObject { ["if"] = new JArray("@not.a.binding", 1, 5) },
                ["$context"] = new JObject { ["tier"] = "@config(\"/plan\")" },
            },
        };

        foreach (var property in bindings.Properties())
        {
            composition.SourceContent(Compositions.Context)[property.Name] = property.Value.DeepClone();
            composition.Content()[property.Name] = property.Value.DeepClone();
            composition.AddOrigin($"/{property.Name}", 5);
        }

        return composition;
    }

    private static Sdk.MachineAccess Machine(string id, string? annotationKey, params Sdk.MachineScope[] scopes)
    {
        return new Sdk.MachineAccess { Id = id, AnnotationKey = annotationKey, Scopes = scopes, AccessKey = "2s.masked" };
    }
}
