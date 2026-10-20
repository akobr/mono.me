using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The origins carried by the real merge (MergeInto); no emulator.
public class ConfigurationCompositionOriginsTests
{
    [Fact]
    public void Mark_AnnotatesEveryValueAndLeavesTheInputUntouched()
    {
        // without line info, which Newtonsoft keeps as annotations, so the input starts without any
        var content = JObject.Parse(
            """{ "a": 1, "b": { "c": [ 1, { "d": "x" } ] }, "e": null }""",
            new JsonLoadSettings { LineInfoHandling = LineInfoHandling.Ignore });
        var original = content.DeepClone();

        var marked = ConfigurationCompositionOrigins.Mark(content, 3);

        JToken.DeepEquals(marked, content).Should().BeTrue();
        marked.Should().NotBeSameAs(content);
        Origins(marked).Should().Equal(("/a", 3), ("/b", 3), ("/b/c", 3), ("/b/c/0", 3), ("/b/c/1", 3), ("/b/c/1/d", 3), ("/e", 3));
        JToken.DeepEquals(content, original).Should().BeTrue();
        content.DescendantsAndSelf().Count(token => token.Annotations<object>().Any()).Should().Be(0);
    }

    [Fact]
    public void Collect_UnannotatedValue_Throws()
    {
        var marked = ConfigurationCompositionOrigins.Mark(JObject.Parse("""{ "a": { "b": 1 } }"""), 1);
        ((JObject)marked["a"]!).Add("c", 2);

        var collect = () => ConfigurationCompositionOrigins.Collect(marked);

        collect.Should().Throw<InvalidOperationException>().WithMessage("*/a/c*");
        ((Action)(() => ConfigurationCompositionOrigins.Collect(JObject.Parse("""{ "a": 1 }"""))))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Strip_RemovesTheSteps()
    {
        var marked = ConfigurationCompositionOrigins.Mark(JObject.Parse("""{ "a": [ { "b": 1 } ] }"""), 2);

        var stripped = ConfigurationCompositionOrigins.Strip(marked);

        JToken.DeepEquals(stripped, marked).Should().BeTrue();
        stripped.DescendantsAndSelf().Count(token => token.Annotations<object>().Any()).Should().Be(0);
        Origins(marked).Should().HaveCount(3);
    }

    [Fact]
    public void Collect_Pointers_EscapeNamesAndIndexArraysWithoutTheRoot()
    {
        var content = JObject.Parse("""
                                    {
                                        "a/b": 1,
                                        "m~n": { "x": [ 10, { "y": null }, [] ] },
                                        "": 2,
                                        "~/": {}
                                    }
                                    """);

        var origins = ConfigurationCompositionOrigins.Collect(ConfigurationCompositionOrigins.Mark(content, 4));

        origins.Select(origin => origin.Path).Should().Equal(
            "/a~1b",
            "/m~0n",
            "/m~0n/x",
            "/m~0n/x/0",
            "/m~0n/x/1",
            "/m~0n/x/1/y",
            "/m~0n/x/2",
            "/",
            "/~0~1");
        origins.Should().OnlyContain(origin => origin.Step == 4);
        origins.Select(origin => origin.Path).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Merge_ObjectIntoObject_KeepsTheContainerAndAttributesItsProperties()
    {
        var target = Applied(("""{ "a": { "x": 1 } }""", 1), ("""{ "a": { "y": 2 } }""", 2));

        target.ToString(Formatting.None).Should().Be("""{"a":{"x":1,"y":2}}""");
        Origins(target).Should().Equal(("/a", 1), ("/a/x", 1), ("/a/y", 2));
    }

    [Fact]
    public void Merge_EqualScalar_KeepsTheEarlierOrigin()
    {
        var target = Applied(("""{ "n": 1, "s": "v", "b": true }""", 1), ("""{ "n": 1, "s": "v", "b": true }""", 2));

        Origins(target).Should().Equal(("/n", 1), ("/s", 1), ("/b", 1));
    }

    [Fact]
    public void Merge_IntegerAgainstFloat_TakesTheIncomingOrigin()
    {
        var target = Applied(("""{ "n": 1 }""", 1), ("""{ "n": 1.0 }""", 2));

        target["n"]!.Type.Should().Be(JTokenType.Float);
        Origins(target).Should().Equal(("/n", 2));
    }

    [Fact]
    public void Merge_DifferentScalar_TakesTheIncomingOrigin()
    {
        var target = Applied(("""{ "n": 1, "s": "v" }""", 1), ("""{ "n": 2, "s": "w" }""", 2));

        Origins(target).Should().Equal(("/n", 2), ("/s", 2));
    }

    [Fact]
    public void Merge_NullOnAnExistingProperty_IsIgnored()
    {
        var target = Applied(("""{ "a": 1, "b": { "c": 1 } }""", 1), ("""{ "a": null, "b": null }""", 2));

        target.ToString(Formatting.None).Should().Be("""{"a":1,"b":{"c":1}}""");
        Origins(target).Should().Equal(("/a", 1), ("/b", 1), ("/b/c", 1));
    }

    [Fact]
    public void Merge_NullOnAnAbsentProperty_TakesTheIncomingOrigin()
    {
        var target = Applied(("""{ "a": 1 }""", 1), ("""{ "b": null }""", 2));

        target["b"]!.Type.Should().Be(JTokenType.Null);
        Origins(target).Should().Equal(("/a", 1), ("/b", 2));
    }

    [Fact]
    public void Merge_ArrayIntoArray_KeepsTheExistingItemsAndAppendsTheOthers()
    {
        var target = Applied(
            ("""{ "arr": [ 1, 1, { "k": 2 } ] }""", 1),
            ("""{ "arr": [ { "k": 2 }, 3, 3, 1, { "k": 4 } ] }""", 2));

        target.ToString(Formatting.None).Should().Be("""{"arr":[1,1,{"k":2},3,{"k":4}]}""");
        Origins(target).Should().Equal(
            ("/arr", 1),
            ("/arr/0", 1),
            ("/arr/1", 1),
            ("/arr/2", 1),
            ("/arr/2/k", 1),
            ("/arr/3", 2),
            ("/arr/4", 2),
            ("/arr/4/k", 2));
    }

    [Fact]
    public void Merge_ArrayOnAnAbsentProperty_KeepsItsDuplicates()
    {
        var target = Applied(("""{ "a": 1 }""", 1), ("""{ "arr": [ 5, 5 ] }""", 2));

        target.ToString(Formatting.None).Should().Be("""{"a":1,"arr":[5,5]}""");
        Origins(target).Should().Equal(("/a", 1), ("/arr", 2), ("/arr/0", 2), ("/arr/1", 2));
    }

    [Fact]
    public void Merge_DifferentTypes_ReplaceTheWholeSubtree()
    {
        var target = Applied(
            ("""{ "a": { "x": 1 }, "b": 5, "c": [ 1 ] }""", 1),
            ("""{ "a": 7, "b": { "y": 2 }, "c": { "z": 3 } }""", 2));

        target.ToString(Formatting.None).Should().Be("""{"a":7,"b":{"y":2},"c":{"z":3}}""");
        Origins(target).Should().Equal(("/a", 2), ("/b", 2), ("/b/y", 2), ("/c", 2), ("/c/z", 2));
    }

    [Fact]
    public void Merge_FinishedDocumentOfAParent_KeepsTheOriginsOfItsCalculation()
    {
        // the target holds a: { x: 1 }; the parent is composed of its template a: 5 and its own a: { y: 2 }
        var target = Applied(("""{ "a": { "x": 1 } }""", 1));
        var parent = Applied(("""{ "a": 5 }""", 2), ("""{ "a": { "y": 2 } }""", 3));

        target.MergeInto(parent);

        target.ToString(Formatting.None).Should().Be("""{"a":{"x":1,"y":2}}""");
        Origins(target).Should().Equal(("/a", 1), ("/a/x", 1), ("/a/y", 3));
        Origins(parent).Should().Equal(("/a", 3), ("/a/y", 3));

        // the merge is not associative: the parent's sources replayed flat give another document
        var flat = Applied(("""{ "a": { "x": 1 } }""", 1), ("""{ "a": 5 }""", 2), ("""{ "a": { "y": 2 } }""", 3));
        flat.ToString(Formatting.None).Should().Be("""{"a":{"y":2}}""");
    }

    // Merges each document, marked with its step, into an empty document.
    private static JObject Applied(params (string Json, int Step)[] documents)
    {
        var target = new JObject();

        foreach (var (json, step) in documents)
        {
            target.MergeInto(ConfigurationCompositionOrigins.Mark(JObject.Parse(json), step));
        }

        return target;
    }

    private static IEnumerable<(string Path, int Step)> Origins(JObject content)
    {
        return ConfigurationCompositionOrigins.Collect(content).Select(origin => (origin.Path, origin.Step));
    }
}
