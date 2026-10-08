using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

public class ConfigurationBindingResolverTests
{
    private readonly FakeStringBinding _strings = new();
    private readonly ConfigurationBindingResolver _resolver;
    private readonly BindingScope _scope;

    public ConfigurationBindingResolverTests()
    {
        _resolver = new ConfigurationBindingResolver(_strings);
        _scope = new BindingScope { Document = new JObject { ["plan"] = new JObject { ["tier"] = 1 } } };
    }

    [Fact]
    public async Task Resolve_JsonLogicNumber_ReplacesEnvelopeWithNumber()
    {
        var content = Parse("""
            {
              "retries": {
                "$binding": "jlogic",
                "$definition": { "+": [1, 2] }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["retries"]!.Type.Should().BeOneOf(JTokenType.Integer, JTokenType.Float);
        content["retries"]!.Value<decimal>().Should().Be(3);
    }

    [Fact]
    public async Task Resolve_JsonLogicNull_ReplacesEnvelopeWithNull()
    {
        var content = Parse("""
            {
              "missing": {
                "$binding": "jlogic",
                "$definition": { "var": "absent" }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["missing"]!.Type.Should().Be(JTokenType.Null);
    }

    [Fact]
    public async Task Resolve_JsonLogicArray_ReplacesEnvelopeWithArray()
    {
        var content = Parse("""
            {
              "nums": {
                "$binding": "jlogic",
                "$definition": { "merge": [[1, 2], [3]] }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var nums = content["nums"].Should().BeOfType<JArray>().Subject;
        nums.Values<decimal>().Should().Equal(1m, 2m, 3m);
    }

    [Fact]
    public async Task Resolve_JsonEObject_ReplacesEnvelopeWithObject()
    {
        var content = Parse("""
            {
              "connection": {
                "$binding": "jsone",
                "$definition": { "host": { "$eval": "host" } },
                "$context": { "host": "db.internal" }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["connection"].Should().BeOfType<JObject>();
        content["connection"]!["host"]!.Value<string>().Should().Be("db.internal");
    }

    [Fact]
    public async Task Resolve_MissingContext_UsesEmptyObject()
    {
        var content = Parse("""
            {
              "value": {
                "$binding": "jlogic",
                "$definition": { "var": ["absent", 4] }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(4);
    }

    [Theory]
    [InlineData("""{ "value": { "$binding": "jlogic", "$definition": 1, "note": true } }""")]
    [InlineData("""{ "value": { "$binding": "jsone" } }""")]
    [InlineData("""{ "value": { "$binding": "json-e", "$definition": 1 } }""")]
    public async Task Resolve_MalformedEnvelope_ThrowsAndLeavesToken(string json)
    {
        var content = Parse(json);
        var before = content.ToString(Newtonsoft.Json.Formatting.None);

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
        content.ToString(Newtonsoft.Json.Formatting.None).Should().Be(before);
    }

    [Fact]
    public async Task Resolve_UnrelatedBindingProperty_WalksChildren()
    {
        var content = Parse("""
            {
              "meta": { "$binding": "manual", "name": "@name" }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["meta"]!["$binding"]!.Value<string>().Should().Be("manual");
        content["meta"]!["name"]!.Value<string>().Should().Be("resolved:@name");
    }

    [Fact]
    public async Task Resolve_ContextIsResolvedBeforeEngine()
    {
        var content = Parse("""
            {
              "retries": {
                "$binding": "jlogic",
                "$definition": { "+": [{ "var": "tier" }, { "var": "nested" }] },
                "$context": {
                  "tier": 1,
                  "label": "@tier",
                  "nested": {
                    "$binding": "jlogic",
                    "$definition": { "+": [10, 1] }
                  }
                }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["retries"]!.Value<decimal>().Should().Be(12);
        _strings.Calls.Should().ContainSingle(call => call.Raw == "@tier");
        content.SelectToken("retries.$context")?.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_DefinitionIsNotResolvedBeforeEngine()
    {
        var content = Parse("""
            {
              "value": {
                "$binding": "jlogic",
                "$definition": { "==": [1, "@only-in-definition"] },
                "$context": { "secret": "@secret" }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Type.Should().Be(JTokenType.Boolean);
        content["value"]!.Value<bool>().Should().BeFalse();
        _strings.Calls.Select(call => call.Raw).Should().Equal("@secret");
    }

    [Fact]
    public async Task Resolve_NestedEnvelopeInsideDefinition_IsNotResolvedBeforeEngine()
    {
        var content = Parse("""
            {
              "value": {
                "$binding": "jsone",
                "$definition": {
                  "kept": {
                    "$binding": "jlogic",
                    "$definition": 1,
                    "$context": { "s": "@hidden" }
                  }
                }
              }
            }
            """);

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>();
        _strings.Calls.Select(call => call.Raw).Should().NotContain("@hidden");
    }

    [Fact]
    public async Task Resolve_JsonEEscapedEnvelope_IsResolvedByPostPass()
    {
        var content = Parse("""
            {
              "value": {
                "$binding": "jsone",
                "$definition": {
                  "$$binding": "jlogic",
                  "$$definition": { "+": [2, 2] }
                }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(4);
    }

    [Fact]
    public async Task Resolve_ResultString_IsBoundByPostPass()
    {
        var content = Parse("""
            {
              "value": {
                "$binding": "jlogic",
                "$definition": "@name"
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("resolved:@name");
    }

    [Fact]
    public async Task Resolve_RootEnvelopeObject_ReplacesContentsOfSameInstance()
    {
        var content = Parse("""
            {
              "$binding": "jsone",
              "$definition": { "ok": true }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content.Property("$binding").Should().BeNull();
        content["ok"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_RootEnvelopeScalar_ThrowsAndLeavesToken()
    {
        var content = Parse("""
            {
              "$binding": "jlogic",
              "$definition": "hello"
            }
            """);
        var before = content.ToString(Newtonsoft.Json.Formatting.None);

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
        content.ToString(Newtonsoft.Json.Formatting.None).Should().Be(before);
    }

    [Fact]
    public async Task Resolve_EnvelopeChainOf32_Succeeds()
    {
        var content = new JObject { ["value"] = Chain(32) };

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("done");
    }

    [Fact]
    public async Task Resolve_EnvelopeChainOf33_Throws()
    {
        var content = new JObject { ["value"] = Chain(33) };

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task Resolve_EnvelopeFanOutOfTwo_ResolvesBothBranches()
    {
        var content = new JObject { ["value"] = FanOut(3) };

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JArray>().Subject;
        value.Should().HaveCount(2);
        value[0].Values<string>().Should().Equal("done", "done");
        value[1].Values<string>().Should().Equal("done", "done");
    }

    [Fact]
    public async Task Resolve_EnvelopeFanOutPastEvaluationBudget_Throws()
    {
        var levels = 1;
        while ((1 << levels) - 1 <= ConfigurationBindingResolver.MaxEnvelopeEvaluations)
        {
            levels++;
        }

        var content = new JObject { ["value"] = FanOut(levels) };
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>()
            .WithMessage($"*exceed {ConfigurationBindingResolver.MaxEnvelopeEvaluations} evaluations*");
    }

    [Fact]
    public async Task Resolve_JsonLogicCat_ConcatenatesStrings()
    {
        var content = Parse("""
            {
              "name": {
                "$binding": "jlogic",
                "$definition": { "cat": ["a", "b"] }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["name"]!.Value<string>().Should().Be("ab");
    }

    [Fact]
    public async Task Resolve_JsonLogicReduceDoublingPastConcatLimit_Throws()
    {
        var content = new JObject
        {
            ["value"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = new JObject
                {
                    ["reduce"] = new JArray
                    {
                        DoublingSteps("ab"),
                        new JObject
                        {
                            ["cat"] = new JArray
                            {
                                new JObject { ["var"] = "accumulator" },
                                new JObject { ["var"] = "accumulator" },
                            },
                        },
                        "ab",
                    },
                },
            },
        };

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*cat exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonLogicMergePastItemLimit_Throws()
    {
        var items = new JArray();
        for (var index = 0; index < ConfigurationBindingResolver.MaxMergeItems + 1; index++)
        {
            items.Add(index);
        }

        var content = new JObject
        {
            ["value"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = new JObject
                {
                    ["merge"] = new JArray { items },
                },
            },
        };

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*merge exceeds {ConfigurationBindingResolver.MaxMergeItems} items*");
    }

    [Fact]
    public async Task Resolve_JsonERangeWithinLimit_ReturnsLength()
    {
        var content = Envelope("jsone", new JObject
        {
            ["$eval"] = $"len(range(0, {ConfigurationBindingResolver.MaxRangeItems}))",
        });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(ConfigurationBindingResolver.MaxRangeItems);
    }

    [Fact]
    public async Task Resolve_JsonERangePastItemLimit_Throws()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["$eval"] = $"len(range(0, {ConfigurationBindingResolver.MaxRangeItems + 1}))",
            },
            new JObject { ["range"] = "nope" });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*range exceeds {ConfigurationBindingResolver.MaxRangeItems} items*");
    }

    [Fact]
    public async Task Resolve_JsonEMapOverRangePastItemLimit_Throws()
    {
        var content = Envelope("jsone", new JObject
        {
            ["$map"] = new JObject
            {
                ["$eval"] = $"range(0, {ConfigurationBindingResolver.MaxRangeItems + 1})",
            },
            ["each(x)"] = true,
        });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*range exceeds {ConfigurationBindingResolver.MaxRangeItems} items*");
    }

    [Fact]
    public async Task Resolve_JsonENestedLetWithinDepth_DoublesString()
    {
        var content = Envelope("jsone", DoublingLets(3));

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("aaaaaaaa");
    }

    [Fact]
    public async Task Resolve_JsonENestedLetPastOperatorDepth_Throws()
    {
        var content = Envelope("jsone", DoublingLets(ConfigurationBindingResolver.MaxJsonEOperatorDepth));

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>()
            .WithMessage($"*JSON-e template nesting exceeds {ConfigurationBindingResolver.MaxJsonEOperatorDepth}*");
    }

    [Fact]
    public async Task Resolve_JsonEAdditionRespectsPrecedence()
    {
        var content = Envelope("jsone", new JObject { ["$eval"] = "1+2*3" });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(7);
    }

    [Fact]
    public async Task Resolve_JsonEInterpolation_ResolvesName()
    {
        var content = Envelope("jsone", Parse("""
            {
              "$let": { "name": "db" },
              "in": "${name}"
            }
            """));

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("db");
    }

    [Fact]
    public async Task Resolve_JsonEReduceDoublingPastConcatLimit_Throws()
    {
        var steps = DoublingSteps("ab").Count;
        var content = Envelope("jsone", new JObject
        {
            ["$reduce"] = new JObject { ["$eval"] = $"range(0, {steps})" },
            ["initial"] = "ab",
            ["each(acc,v)"] = new JObject { ["$eval"] = "acc+acc" },
        });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonEInterpolationDoublingPastConcatLimit_Throws()
    {
        var steps = DoublingSteps("ab").Count;
        var content = Envelope("jsone", new JObject
        {
            ["$reduce"] = new JObject { ["$eval"] = $"range(0, {steps})" },
            ["initial"] = "ab",
            ["each(acc,v)"] = "${acc}${acc}",
        });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonEReduceArrayDoublingPastValueSize_Throws()
    {
        var steps = StepsUntilArrayExceedsConcatLimit();
        steps.Should().BeInRange(1, 16);
        var content = Envelope("jsone", new JObject
        {
            ["$reduce"] = new JObject { ["$eval"] = $"range(0, {steps})" },
            ["initial"] = "x",
            ["each(acc,i)"] = new JObject { ["$eval"] = "[acc,acc]" },
        });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonLogicReduceArrayDoublingPastValueSize_Throws()
    {
        var steps = StepsUntilArrayExceedsConcatLimit();
        steps.Should().BeInRange(1, 16);
        var items = new JArray();
        for (var index = 0; index < steps; index++)
        {
            items.Add(index);
        }

        var content = new JObject
        {
            ["value"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = new JObject
                {
                    ["reduce"] = new JArray
                    {
                        items,
                        new JArray
                        {
                            new JObject { ["var"] = "accumulator" },
                            new JObject { ["var"] = "accumulator" },
                        },
                        "x",
                    },
                },
            },
        };

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*reduce value exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonERootListLiteralPastValueSize_Throws()
    {
        // Two copies cross the cap. Repeating a 90000-character seed hundreds of times is the reported case.
        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "[s,s]" },
            new JObject { ["s"] = new string('x', (ConfigurationBindingResolver.MaxConcatLength / 2) + 1) });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*result exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonLogicArrayLiteralPastValueSize_Throws()
    {
        var content = Envelope(
            "jlogic",
            new JArray
            {
                new JObject { ["var"] = "s" },
                new JObject { ["var"] = "s" },
            },
            new JObject { ["s"] = new string('x', (ConfigurationBindingResolver.MaxConcatLength / 2) + 1) });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*result exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonENestedListLiteralPastValueSize_Throws()
    {
        const int terms = 12_000;
        var seedLength = 1;
        while (ListLiteralLength(seedLength, terms) <= ConfigurationBindingResolver.MaxConcatLength)
        {
            seedLength++;
        }

        // A 90000-character seed is the reported case. This seed keeps an unchecked result near the cap.
        seedLength.Should().BeInRange(1, 32);
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["a"] = new JObject { ["$eval"] = RepeatedExpression("s", terms) },
            },
            new JObject { ["s"] = new string('x', seedLength) });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonEJsonArrayDoublingPastValueSize_Throws()
    {
        var steps = StepsUntilJsonDoublingExceedsBudget();
        steps.Should().BeInRange(1, 10);
        var content = Envelope("jsone", new JObject
        {
            ["$reduce"] = new JObject { ["$eval"] = $"range(0, {steps})" },
            ["initial"] = "x",
            ["each(acc,i)"] = new JObject
            {
                ["$json"] = new JObject { ["$eval"] = "[acc,acc]" },
            },
        });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonEJoinDoublingPastValueSize_Throws()
    {
        var steps = StepsUntilStringExceedsConcatLimit();
        steps.Should().BeInRange(1, 18);
        var content = Envelope("jsone", new JObject
        {
            ["$reduce"] = new JObject { ["$eval"] = $"range(0, {steps})" },
            ["initial"] = "x",
            ["each(acc,i)"] = new JObject { ["$eval"] = "join([acc,acc],'')" },
        });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonENestedMapPastValueSize_Throws()
    {
        var content = Envelope("jsone", NestedMaps(3));

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonEJoin_JoinsStrings()
    {
        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "join(['a','b'],'-')" },
            new JObject { ["join"] = "nope" });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("a-b");
    }

    [Fact]
    public async Task Resolve_JsonEReduceArrayPair_ReturnsBothCopies()
    {
        var content = Envelope("jsone", new JObject
        {
            ["$reduce"] = new JObject { ["$eval"] = "range(0, 1)" },
            ["initial"] = "x",
            ["each(acc,i)"] = new JObject { ["$eval"] = "[acc,acc]" },
        });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JArray>().Subject;
        value.Should().HaveCount(2);
        value[0]!.Value<string>().Should().Be("x");
        value[1]!.Value<string>().Should().Be("x");
    }

    [Fact]
    public async Task Resolve_JsonEJson_ReturnsSerializedArray()
    {
        var content = Envelope("jsone", new JObject
        {
            ["$json"] = new JArray("x", "x"),
        });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("""["x","x"]""");
    }

    [Fact]
    public async Task Resolve_JsonEMapIfWithoutElse_DropsFalseItems()
    {
        var content = Envelope("jsone", new JObject
        {
            ["$map"] = new JArray(1, 2, 3),
            ["each(x)"] = new JObject
            {
                ["$if"] = "x > 1",
                ["then"] = new JObject { ["$eval"] = "x" },
            },
        });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JArray>().Subject;
        value.Select(item => item!.Value<decimal>()).Should().Equal(2m, 3m);
    }

    [Fact]
    public async Task Resolve_JsonESwitchDefault_ReturnsDefault()
    {
        var content = Envelope(
            "jsone",
            Parse("""
                {
                  "$switch": {
                    "x == 2": "two",
                    "$default": "other"
                  }
                }
                """),
            new JObject { ["x"] = 1 });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("other");
    }

    [Fact]
    public async Task Resolve_JsonEIfPlus_ReturnsThen()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["$if"] = "s+'b' == 'ab'",
                ["then"] = 1,
                ["else"] = 0,
            },
            new JObject { ["s"] = "a" });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(1);
    }

    [Fact]
    public async Task Resolve_JsonEIfPlusPastConcatLimit_Throws()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["$if"] = "len(s+s) > 0",
                ["then"] = 1,
                ["else"] = 0,
            },
            new JObject { ["s"] = new string('x', (ConfigurationBindingResolver.MaxConcatLength / 2) + 1) });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*string concatenation exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonESwitchPlusKey_ReturnsMatch()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["$switch"] = new JObject
                {
                    ["s+'b' == 'ab'"] = 1,
                    ["$default"] = 0,
                },
            },
            new JObject { ["s"] = "a" });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(1);
    }

    [Fact]
    public async Task Resolve_JsonESwitchPlusKeyPastConcatLimit_Throws()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["$switch"] = new JObject
                {
                    ["len(s+s) > 0"] = 1,
                    ["$default"] = 0,
                },
            },
            new JObject { ["s"] = new string('x', (ConfigurationBindingResolver.MaxConcatLength / 2) + 1) });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*string concatenation exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonESortByPlus_OrdersByKey()
    {
        var content = Envelope("jsone", new JObject
        {
            ["$sort"] = new JArray
            {
                new JObject { ["a"] = 2 },
                new JObject { ["a"] = 1 },
            },
            ["by(x)"] = "x.a + 10",
        });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JArray>().Subject;
        value.Select(item => item!["a"]!.Value<decimal>()).Should().Equal(1m, 2m);
    }

    [Fact]
    public async Task Resolve_JsonEFindEachPlus_ReturnsFirstMatch()
    {
        var content = Envelope("jsone", new JObject
        {
            ["$find"] = new JArray("aa", "b"),
            ["each(x)"] = "len(x+x) > 3",
        });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("aa");
    }

    [Fact]
    public async Task Resolve_JsonEMatchPlusKey_ReturnsMatch()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["$match"] = new JObject
                {
                    ["s+'b' == 'ab'"] = "yes",
                },
            },
            new JObject { ["s"] = "a" });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JArray>().Subject;
        value.Select(item => item!.Value<string>()).Should().Equal("yes");
    }

    [Fact]
    public async Task Resolve_JsonEInterpolatedKey_RendersName()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["${s+'b'}"] = 1,
                ["pre-${s}-mid}{-${s+'}'}"] = 2,
            },
            new JObject { ["s"] = "a" });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JObject>().Subject;
        value["ab"]!.Value<decimal>().Should().Be(1);
        value["pre-a-mid}{-a}"]!.Value<decimal>().Should().Be(2);
    }

    [Fact]
    public async Task Resolve_JsonEInterpolatedKeyPastConcatLimit_Throws()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["${len(s+s)}"] = 1,
            },
            new JObject { ["s"] = new string('x', (ConfigurationBindingResolver.MaxConcatLength / 2) + 1) });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*string concatenation exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonEInterpolation_KeepsApostropheAndBackslash()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["greeting"] = "it's ${name}",
                ["path"] = "C:\\data\\${env}",
            },
            new JObject
            {
                ["name"] = "a",
                ["env"] = "x",
            });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JObject>().Subject;
        value["greeting"]!.Value<string>().Should().Be("it's a");
        value["path"]!.Value<string>().Should().Be("C:\\data\\x");
    }

    [Fact]
    public async Task Resolve_JsonEInterpolatedKey_KeepsApostropheBackslashAndDollarEscape()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["it's ${name}"] = 1,
                ["C:\\data\\${env}"] = 2,
                ["$$a${name}"] = 3,
            },
            new JObject
            {
                ["name"] = "a",
                ["env"] = "x",
            });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JObject>().Subject;
        value["it's a"]!.Value<decimal>().Should().Be(1);
        value["C:\\data\\x"]!.Value<decimal>().Should().Be(2);
        value["$aa"]!.Value<decimal>().Should().Be(3);
    }

    [Fact]
    public async Task Resolve_JsonEInterpolation_KeepsEscapedDollar()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["mixed"] = "$${x} and ${x}",
                ["nested"] = "$${a ${s}}",
            },
            new JObject
            {
                ["x"] = 1.5,
                ["s"] = "ab",
            });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JObject>().Subject;
        value["mixed"]!.Value<string>().Should().Be("${x} and 1.5");
        value["nested"]!.Value<string>().Should().Be("${a ab}");
    }

    [Fact]
    public async Task Resolve_JsonEInterpolatedKey_KeepsEscapedDollar()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["pre$${x} and ${x}"] = 1,
            },
            new JObject { ["x"] = 1.5 });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JObject>().Subject;
        value["pre${x} and 1.5"]!.Value<decimal>().Should().Be(1);
    }

    [Fact]
    public async Task Resolve_JsonEInterpolatedKey_OnlyEscapedHole_IsBounded()
    {
        var rendered = Envelope(
            "jsone",
            new JObject { ["$${x}"] = 1 },
            new JObject { ["x"] = 1.5 });

        await _resolver.ResolveAsync(rendered, includeSecrets: true, _scope);

        var renderedValue = rendered["value"].Should().BeOfType<JObject>().Subject;
        renderedValue["1.5"]!.Value<decimal>().Should().Be(1);

        var content = Envelope(
            "jsone",
            new JObject { ["$${len(s+s)}"] = 1 },
            new JObject { ["s"] = new string('x', (ConfigurationBindingResolver.MaxConcatLength / 2) + 1) });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*string concatenation exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonESlicePlus_ReturnsEachSlice()
    {
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["$let"] = new JObject { ["s"] = "abc" },
                ["in"] = new JArray
                {
                    new JObject { ["$eval"] = "s[0] + 'x'" },
                    new JObject { ["$eval"] = "s[0:2] + 'x'" },
                    new JObject { ["$eval"] = "s[:2] + 'x'" },
                    new JObject { ["$eval"] = "s[1:] + 'x'" },
                },
            });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JArray>().Subject;
        value.Select(item => item!.Value<string>()).Should().Equal("ax", "abx", "abx", "bcx");
    }

    [Fact]
    public async Task Resolve_JsonEInterpolationSlicePlus_ReturnsPrefix()
    {
        var content = Envelope(
            "jsone",
            "${name[0:1] + '.'}",
            new JObject { ["name"] = "ab" });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("a.");
    }

    [Fact]
    public async Task Resolve_JsonEObjectLiteralPlus_ReturnsObject()
    {
        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "{a: n + 1, 'b': n + 2}" },
            new JObject { ["n"] = 2 });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JObject>().Subject;
        value["a"]!.Value<decimal>().Should().Be(3);
        value["b"]!.Value<decimal>().Should().Be(4);
    }

    [Fact]
    public async Task Resolve_JsonELetFanOutPastValueSize_Throws()
    {
        const int copies = 10;
        var levels = LetFanOutLevels(copies, seedLength: 100);
        levels.Should().BeInRange(1, 4);
        var content = Envelope("jsone", LetFanOut(levels, copies, new string('x', 100)));

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonLogicMap_RepeatsLiteral()
    {
        var content = new JObject
        {
            ["value"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = new JObject
                {
                    ["map"] = new JArray
                    {
                        new JArray(1, 2),
                        "a",
                    },
                },
            },
        };

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JArray>().Subject;
        value.Select(item => item!.Value<string>()).Should().Equal("a", "a");
    }

    [Fact]
    public async Task Resolve_JsonLogicNestedMapPastValueSize_Throws()
    {
        // 30 keeps an unchecked result under a megabyte. 200 is the reported multi-gigabyte case.
        const int count = 30;
        count.Should().BeLessThan(40);
        var content = new JObject
        {
            ["value"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = NestedJsonLogicMaps(3, count, "xxxxxxxxxx"),
            },
        };

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*map exceeds {ConfigurationBindingResolver.MaxConcatLength} characters*");
    }

    [Fact]
    public async Task Resolve_JsonLogicAll_ReturnsTrue()
    {
        var content = new JObject
        {
            ["value"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = new JObject
                {
                    ["all"] = new JArray
                    {
                        new JArray(1, 2),
                        true,
                    },
                },
            },
        };

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_JsonLogicNestedAllPastIterationLimit_Throws()
    {
        var width = CubicWidthPastStepLimit();
        width.Should().BeInRange(1, 50);
        var content = new JObject
        {
            ["value"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = NestedAll(3, width),
            },
        };

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*exceeds {ConfigurationBindingResolver.MaxEvaluationSteps} steps*");
    }

    [Fact]
    public async Task Resolve_SiblingEnvelopesShareStepLimit_Throws()
    {
        var width = CubicWidthPastStepLimit() - 1;
        width.Should().BeInRange(1, 49);
        var one = (long)width + ((long)width * width) + ((long)width * width * width);
        one.Should().BeLessThanOrEqualTo(ConfigurationBindingResolver.MaxEvaluationSteps);
        (one * 2).Should().BeGreaterThan(ConfigurationBindingResolver.MaxEvaluationSteps);

        var single = new JObject
        {
            ["value"] = new JObject
            {
                ["$binding"] = "jsone",
                ["$definition"] = NestedDeletedMaps(3, width),
            },
        };
        await _resolver.ResolveAsync(single, includeSecrets: true, _scope);

        var content = new JObject
        {
            ["first"] = new JObject
            {
                ["$binding"] = "jsone",
                ["$definition"] = NestedDeletedMaps(3, width),
            },
            ["second"] = new JObject
            {
                ["$binding"] = "jlogic",
                ["$definition"] = NestedAll(3, width),
            },
        };

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*exceeds {ConfigurationBindingResolver.MaxEvaluationSteps} steps*");
    }

    [Fact]
    public async Task Resolve_JsonENestedMapDeletedPastStepLimit_Throws()
    {
        var width = CubicWidthPastStepLimit();
        width.Should().BeInRange(1, 50);
        var content = Envelope("jsone", NestedDeletedMaps(3, width));

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingException>()
            .WithMessage($"*exceeds {ConfigurationBindingResolver.MaxEvaluationSteps} steps*");
    }

    [Fact]
    public async Task Resolve_ForwardsIncludeSecretsAndScope()
    {
        var content = Parse("""{ "name": "@name" }""");

        await _resolver.ResolveAsync(content, includeSecrets: false, _scope);

        _strings.Calls.Should().ContainSingle();
        _strings.Calls[0].IncludeSecrets.Should().BeFalse();
        _strings.Calls[0].Scope.Should().BeSameAs(_scope);
        content["name"]!.Value<string>().Should().Be("resolved:@name");
    }

    [Fact]
    public async Task Resolve_LeavesScopeDocumentUnchanged()
    {
        var before = _scope.Document!.DeepClone();
        var content = Parse("""
            {
              "value": {
                "$binding": "jlogic",
                "$definition": { "var": "plan.tier" },
                "$context": { "plan": { "tier": "@tier" } }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        JToken.DeepEquals(_scope.Document, before).Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_IsoTimestampStaysAString()
    {
        var content = Parse("""
            {
              "when": {
                "$binding": "jsone",
                "$definition": { "$fromNow": "1 hour", "from": "2017-01-19T16:27:20.974Z" }
              }
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var when = content["when"]!.Should().BeOfType<JValue>().Subject;
        when.Type.Should().Be(JTokenType.String);
        when.Value<string>().Should().Be("2017-01-19T17:27:20.974Z");
    }

    [Fact]
    public async Task Resolve_JsonEIn_ChargesScan()
    {
        var found = Envelope("jsone", new JObject { ["$eval"] = "'foo' in 'foobar'" });
        await _resolver.ResolveAsync(found, includeSecrets: true, _scope);
        found["value"]!.Value<bool>().Should().BeTrue();

        var member = Envelope("jsone", new JObject { ["$eval"] = "'a' in ['b', 'a']" });
        await _resolver.ResolveAsync(member, includeSecrets: true, _scope);
        member["value"]!.Value<bool>().Should().BeTrue();

        var property = Envelope("jsone", new JObject { ["$eval"] = "'foo' in {foo: 1}" });
        await _resolver.ResolveAsync(property, includeSecrets: true, _scope);
        property["value"]!.Value<bool>().Should().BeTrue();

        // One array past the cap. A 2000-term chain over 50000 zeros is the reported case.
        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "1 in xs" },
            new JObject { ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1) });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");

        var text = Envelope(
            "jsone",
            new JObject { ["$eval"] = "'z' in s" },
            new JObject { ["s"] = TextPastStepLimit() });
        var textAct = () => _resolver.ResolveAsync(text, includeSecrets: true, _scope).AsTask();
        await textAct.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonEEquals_ChargesScan()
    {
        var same = Envelope("jsone", new JObject { ["$eval"] = "'ab' == 'ab'" });
        await _resolver.ResolveAsync(same, includeSecrets: true, _scope);
        same["value"]!.Value<bool>().Should().BeTrue();

        var lists = Envelope("jsone", new JObject { ["$eval"] = "[1, 2] == [1, 2]" });
        await _resolver.ResolveAsync(lists, includeSecrets: true, _scope);
        lists["value"]!.Value<bool>().Should().BeTrue();

        var text = Envelope(
            "jsone",
            new JObject { ["$eval"] = "s == s" },
            new JObject { ["s"] = TextPastStepLimit() });
        var textAct = () => _resolver.ResolveAsync(text, includeSecrets: true, _scope).AsTask();
        await textAct.Should().ThrowAsync<BindingException>().WithMessage("*steps*");

        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "xs == ys" },
            new JObject
            {
                ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1),
                ["ys"] = new JArray(0),
            });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonENotEquals_ChargesScan()
    {
        var different = Envelope("jsone", new JObject { ["$eval"] = "'a' != 'b'" });
        await _resolver.ResolveAsync(different, includeSecrets: true, _scope);
        different["value"]!.Value<bool>().Should().BeTrue();

        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "s != s" },
            new JObject { ["s"] = TextPastStepLimit() });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonESplit_ChargesScan()
    {
        var parts = Envelope("jsone", new JObject { ["$eval"] = "split('left:right', ':')" });
        await _resolver.ResolveAsync(parts, includeSecrets: true, _scope);
        parts["value"].Should().BeOfType<JArray>().Which.Select(item => item!.Value<string>()).Should().Equal("left", "right");

        var chars = Envelope("jsone", new JObject { ["$eval"] = "split('ab', '')" });
        await _resolver.ResolveAsync(chars, includeSecrets: true, _scope);
        chars["value"].Should().BeOfType<JArray>().Which.Select(item => item!.Value<string>()).Should().Equal("a", "b");

        var zero = Envelope("jsone", new JObject { ["$eval"] = "split('a0b', 0)" });
        await _resolver.ResolveAsync(zero, includeSecrets: true, _scope);
        zero["value"].Should().BeOfType<JArray>().Which.Select(item => item!.Value<string>()).Should().Equal("a", "b");

        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "split(s, ':')" },
            new JObject { ["s"] = TextPastStepLimit() });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonELowercase_ChargesScan()
    {
        await AssertStringBuiltin("lowercase('Fools!')", "fools!", "lowercase(s)");
    }

    [Fact]
    public async Task Resolve_JsonEUppercase_ChargesScan()
    {
        await AssertStringBuiltin("uppercase('Fools!')", "FOOLS!", "uppercase(s)");
    }

    [Fact]
    public async Task Resolve_JsonEStrip_ChargesScan()
    {
        await AssertStringBuiltin("strip('  room  ')", "room", "strip(s)");
    }

    [Fact]
    public async Task Resolve_JsonELStrip_ChargesScan()
    {
        await AssertStringBuiltin("lstrip('  room  ')", "room  ", "lstrip(s)");
    }

    [Fact]
    public async Task Resolve_JsonERStrip_ChargesScan()
    {
        await AssertStringBuiltin("rstrip('  room  ')", "  room", "rstrip(s)");
    }

    [Fact]
    public async Task Resolve_JsonLogicIn_ChargesScan()
    {
        var found = Envelope("jlogic", Parse("""{ "in": ["foo", "foobar"] }"""));
        await _resolver.ResolveAsync(found, includeSecrets: true, _scope);
        found["value"]!.Value<bool>().Should().BeTrue();

        var member = Envelope("jlogic", Parse("""{ "in": ["a", ["b", "a"]] }"""));
        await _resolver.ResolveAsync(member, includeSecrets: true, _scope);
        member["value"]!.Value<bool>().Should().BeTrue();

        var content = Envelope(
            "jlogic",
            Parse("""{ "in": [1, { "var": "xs" }] }"""),
            new JObject { ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1) });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonLogicEquals_ChargesArrayScan()
    {
        var same = Envelope("jlogic", Parse("""{ "==": [[1, 2], [1, 2]] }"""));
        await _resolver.ResolveAsync(same, includeSecrets: true, _scope);
        same["value"]!.Value<bool>().Should().BeTrue();

        var content = Envelope(
            "jlogic",
            Parse("""{ "==": [{ "var": "xs" }, [0]] }"""),
            new JObject { ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1) });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonLogicNotEquals_ChargesArrayScan()
    {
        var different = Envelope("jlogic", Parse("""{ "!=": [[1], [2]] }"""));
        await _resolver.ResolveAsync(different, includeSecrets: true, _scope);
        different["value"]!.Value<bool>().Should().BeTrue();

        var content = Envelope(
            "jlogic",
            Parse("""{ "!=": [{ "var": "xs" }, [0]] }"""),
            new JObject { ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1) });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_ContextString_IsReadManyTimes()
    {
        // One context string is read on every term. Copying it on each read is the reported cost.
        const int reads = 64;
        var text = new string('b', 50_000);
        var terms = string.Join("||", Enumerable.Repeat("s<'a'", reads));
        var jsone = Envelope(
            "jsone",
            new JObject { ["$eval"] = terms },
            new JObject { ["s"] = text });
        await _resolver.ResolveAsync(jsone, includeSecrets: true, _scope);
        jsone["value"]!.Value<bool>().Should().BeFalse();

        var comparisons = new JArray();
        for (var index = 0; index < reads; index++)
        {
            comparisons.Add(Parse("""{ "==": [{ "var": "s" }, "a"] }"""));
        }

        var logic = Envelope(
            "jlogic",
            new JObject { ["or"] = comparisons },
            new JObject { ["s"] = text });
        await _resolver.ResolveAsync(logic, includeSecrets: true, _scope);
        logic["value"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_JsonELen_ChargesString()
    {
        var sample = Envelope(
            "jsone",
            new JObject
            {
                ["$let"] = new JObject
                {
                    ["s"] = "abc",
                    ["emoji"] = "😀",
                    ["xs"] = new JArray(1, 2, 3),
                },
                ["in"] = new JObject { ["$eval"] = "[len(s), len(emoji), len(xs)]" },
            });
        await _resolver.ResolveAsync(sample, includeSecrets: true, _scope);
        sample["value"].Should().BeOfType<JArray>().Which.Select(item => item!.Value<decimal>()).Should().Equal(3m, 1m, 3m);

        var array = Envelope(
            "jsone",
            new JObject { ["$eval"] = "len(xs)" },
            new JObject { ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1) });
        await _resolver.ResolveAsync(array, includeSecrets: true, _scope);
        array["value"]!.Value<decimal>().Should().Be(ConfigurationBindingResolver.MaxEvaluationSteps + 1);

        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "len(s)" },
            new JObject { ["s"] = TextPastStepLimit() });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonEIndex_ChargesString()
    {
        var sample = Envelope(
            "jsone",
            new JObject
            {
                ["$let"] = new JObject
                {
                    ["s"] = "abc",
                    ["emoji"] = "😀",
                    ["obj"] = new JObject { ["a"] = 1 },
                    ["xs"] = new JArray(10, 20, 30),
                },
                ["in"] = new JObject { ["$eval"] = "[s[0], s[-1], emoji[0], obj['a'], xs[0], xs[-1]]" },
            });
        await _resolver.ResolveAsync(sample, includeSecrets: true, _scope);
        var indexed = sample["value"].Should().BeOfType<JArray>().Subject;
        indexed[0]!.Value<string>().Should().Be("a");
        indexed[1]!.Value<string>().Should().Be("c");
        indexed[2]!.Value<string>().Should().Be("😀");
        indexed[3]!.Value<decimal>().Should().Be(1);
        indexed[4]!.Value<decimal>().Should().Be(10);
        indexed[5]!.Value<decimal>().Should().Be(30);

        var missing = Envelope("jsone", new JObject { ["$eval"] = "{a: 1}['missing']" });
        await _resolver.ResolveAsync(missing, includeSecrets: true, _scope);
        missing["value"]!.Type.Should().Be(JTokenType.Null);

        var bounds = Envelope("jsone", new JObject { ["$eval"] = "'ab'[5]" });
        var boundsAct = () => _resolver.ResolveAsync(bounds, includeSecrets: true, _scope).AsTask();
        await boundsAct.Should().ThrowAsync<BindingException>().WithMessage("*index out of bounds*");

        var array = Envelope(
            "jsone",
            new JObject { ["$eval"] = "xs[0]" },
            new JObject { ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1) });
        await _resolver.ResolveAsync(array, includeSecrets: true, _scope);
        array["value"]!.Value<decimal>().Should().Be(0);

        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "s[0]" },
            new JObject { ["s"] = TextPastStepLimit() });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonESlice_ChargesString()
    {
        var sample = Envelope(
            "jsone",
            new JObject
            {
                ["$let"] = new JObject { ["s"] = "abcde" },
                ["in"] = new JObject { ["$eval"] = "[s[1:3], s[:2], s[2:], s[-2:], s[4:2], s[:-1]]" },
            });
        await _resolver.ResolveAsync(sample, includeSecrets: true, _scope);
        sample["value"].Should().BeOfType<JArray>().Which.Select(item => item!.Value<string>())
            .Should().Equal("bc", "ab", "cde", "de", string.Empty, "abcd");

        var array = Envelope(
            "jsone",
            new JObject { ["$eval"] = "xs[0:1]" },
            new JObject { ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1) });
        await _resolver.ResolveAsync(array, includeSecrets: true, _scope);
        array["value"].Should().BeOfType<JArray>().Which.Select(item => item!.Value<decimal>()).Should().Equal(0m);

        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = "s[1:2]" },
            new JObject { ["s"] = TextPastStepLimit() });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonLogicStrictEquals_ChargesStructure()
    {
        var same = Envelope("jlogic", Parse("""{ "===": [[1, 1], [1, 1]] }"""));
        await _resolver.ResolveAsync(same, includeSecrets: true, _scope);
        same["value"]!.Value<bool>().Should().BeTrue();

        var coerced = Envelope("jlogic", Parse("""{ "===": [1, "1"] }"""));
        await _resolver.ResolveAsync(coerced, includeSecrets: true, _scope);
        coerced["value"]!.Value<bool>().Should().BeFalse();

        var mixed = Envelope(
            "jlogic",
            Parse("""{ "===": [{ "var": "s" }, 1] }"""),
            new JObject { ["s"] = TextPastStepLimit() });
        await _resolver.ResolveAsync(mixed, includeSecrets: true, _scope);
        mixed["value"]!.Value<bool>().Should().BeFalse();

        var text = Envelope(
            "jlogic",
            Parse("""{ "===": [{ "var": "s" }, { "var": "s" }] }"""),
            new JObject { ["s"] = TextPastStepLimit() });
        var textAct = () => _resolver.ResolveAsync(text, includeSecrets: true, _scope).AsTask();
        await textAct.Should().ThrowAsync<BindingException>().WithMessage("*steps*");

        var content = Envelope(
            "jlogic",
            Parse("""{ "===": [{ "var": "xs" }, [0]] }"""),
            new JObject { ["xs"] = NumberArray(ConfigurationBindingResolver.MaxEvaluationSteps + 1) });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonLogicStrictNotEquals_ChargesStructure()
    {
        var different = Envelope("jlogic", Parse("""{ "!==": [[1], [2]] }"""));
        await _resolver.ResolveAsync(different, includeSecrets: true, _scope);
        different["value"]!.Value<bool>().Should().BeTrue();

        var obj = Envelope("jlogic", Parse("""{ "!==": [{ "a": 1 }, { "a": 2 }] }"""));
        await _resolver.ResolveAsync(obj, includeSecrets: true, _scope);
        obj["value"]!.Value<bool>().Should().BeTrue();

        var content = Envelope(
            "jlogic",
            Parse("""{ "!==": [{ "var": "o" }, { "a": "b" }] }"""),
            new JObject { ["o"] = new JObject { ["a"] = TextPastStepLimit() } });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    [Fact]
    public async Task Resolve_JsonLogicLessThan_ChargesString()
    {
        var between = Envelope("jlogic", Parse("""{ "<": [1, 2, 3] }"""));
        await _resolver.ResolveAsync(between, includeSecrets: true, _scope);
        between["value"]!.Value<bool>().Should().BeTrue();

        await AssertComparisonRule("""{ "<": [1, 2] }""", true, """{ "<": [0, { "var": "d" }] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicLessThanOrEqual_ChargesString()
    {
        await AssertComparisonRule("""{ "<=": [1, 1] }""", true, """{ "<=": [0, { "var": "d" }] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicGreaterThan_ChargesString()
    {
        await AssertComparisonRule("""{ ">": [1, 0] }""", true, """{ ">": [{ "var": "d" }, 0] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicGreaterThanOrEqual_ChargesString()
    {
        await AssertComparisonRule("""{ ">=": [1, 1] }""", true, """{ ">=": [{ "var": "d" }, 0] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicAdd_ChargesString()
    {
        await AssertNumericRule("""{ "+": [1, 2] }""", 3, """{ "+": [{ "var": "d" }] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicSubtract_ChargesString()
    {
        await AssertNumericRule("""{ "-": [5, 2] }""", 3, """{ "-": [{ "var": "d" }, 1] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicMultiply_ChargesString()
    {
        await AssertNumericRule("""{ "*": [3, 4] }""", 12, """{ "*": [{ "var": "d" }, 1] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicDivide_ChargesString()
    {
        await AssertNumericRule("""{ "/": [6, 2] }""", 3, """{ "/": [{ "var": "d" }, 1] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicModulo_ChargesString()
    {
        await AssertNumericRule("""{ "%": [5, 2] }""", 1, """{ "%": [{ "var": "d" }, 1] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicMin_ChargesString()
    {
        await AssertNumericRule("""{ "min": [1, 3] }""", 1, """{ "min": [{ "var": "d" }, 1] }""");
    }

    [Fact]
    public async Task Resolve_JsonLogicMax_ChargesString()
    {
        await AssertNumericRule("""{ "max": [1, 3] }""", 3, """{ "max": [{ "var": "d" }, 1] }""");
    }

    [Fact]
    public async Task Resolve_OrdinaryDocument_BindsStringsInPropertiesAndArrayItems()
    {
        var content = Parse("""
            {
              "name": "@name",
              "tags": ["@tag", "plain"],
              "count": 1
            }
            """);

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["name"]!.Value<string>().Should().Be("resolved:@name");
        content["tags"]![0]!.Value<string>().Should().Be("resolved:@tag");
        content["tags"]![1]!.Value<string>().Should().Be("plain");
        content["count"]!.Value<int>().Should().Be(1);
    }

    private async Task AssertComparisonRule(string sample, bool expected, string overCap)
    {
        var rendered = Envelope("jlogic", Parse(sample));
        await _resolver.ResolveAsync(rendered, includeSecrets: true, _scope);
        rendered["value"]!.Value<bool>().Should().Be(expected);

        var content = Envelope(
            "jlogic",
            Parse(overCap),
            new JObject { ["d"] = DigitsPastStepLimit() });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    private async Task AssertNumericRule(string sample, decimal expected, string overCap)
    {
        var rendered = Envelope("jlogic", Parse(sample));
        await _resolver.ResolveAsync(rendered, includeSecrets: true, _scope);
        rendered["value"]!.Value<decimal>().Should().Be(expected);

        var content = Envelope(
            "jlogic",
            Parse(overCap),
            new JObject { ["d"] = DigitsPastStepLimit() });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    private async Task AssertStringBuiltin(string sample, string expected, string overCap)
    {
        var rendered = Envelope("jsone", new JObject { ["$eval"] = sample });
        await _resolver.ResolveAsync(rendered, includeSecrets: true, _scope);
        rendered["value"]!.Value<string>().Should().Be(expected);

        var content = Envelope(
            "jsone",
            new JObject { ["$eval"] = overCap },
            new JObject { ["s"] = TextPastStepLimit() });
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        await act.Should().ThrowAsync<BindingException>().WithMessage("*steps*");
    }

    private static string TextPastStepLimit()
    {
        return new string('a', ConfigurationBindingResolver.MaxEvaluationSteps + 1);
    }

    private static string DigitsPastStepLimit()
    {
        return new string('0', ConfigurationBindingResolver.MaxEvaluationSteps + 1);
    }

    private static JArray NumberArray(int count)
    {
        var items = new JArray();
        for (var index = 0; index < count; index++)
        {
            items.Add(0);
        }

        return items;
    }

    private static JObject Envelope(string kind, JToken definition, JObject? context = null)
    {
        var envelope = new JObject
        {
            ["$binding"] = kind,
            ["$definition"] = definition,
        };
        if (context is not null)
        {
            envelope["$context"] = context;
        }

        return new JObject { ["value"] = envelope };
    }

    private static JToken DoublingLets(int doublings)
    {
        JToken current = new JObject { ["$eval"] = "s" };
        for (var index = 0; index < doublings; index++)
        {
            current = new JObject
            {
                ["$let"] = new JObject
                {
                    ["s"] = new JObject { ["$eval"] = "s+s" },
                },
                ["in"] = current,
            };
        }

        return new JObject
        {
            ["$let"] = new JObject { ["s"] = "a" },
            ["in"] = current,
        };
    }

    private static JArray DoublingSteps(string seed)
    {
        var items = new JArray();
        var length = seed.Length;
        while ((long)length * 2 <= ConfigurationBindingResolver.MaxConcatLength)
        {
            items.Add(items.Count);
            length *= 2;
        }

        items.Add(items.Count);
        return items;
    }

    private static int StepsUntilArrayExceedsConcatLimit()
    {
        var length = 3;
        var steps = 0;
        while (length <= ConfigurationBindingResolver.MaxConcatLength)
        {
            length = (length * 2) + 3;
            steps++;
        }

        return steps;
    }

    private static int StepsUntilStringExceedsConcatLimit()
    {
        var length = 1;
        var steps = 0;
        while (length <= ConfigurationBindingResolver.MaxConcatLength)
        {
            length *= 2;
            steps++;
        }

        return steps;
    }

    private static int StepsUntilJsonDoublingExceedsBudget()
    {
        var length = 1;
        var quotes = 0;
        var slashes = 0;
        var used = 0;
        for (var steps = 1; steps <= 10; steps++)
        {
            var escaped = length + quotes + slashes;
            var arrayLength = (escaped * 2) + 7;
            var arrayQuotes = (quotes * 2) + 4;
            var arraySlashes = (quotes * 2) + (slashes * 4);
            var quoted = arrayLength + arrayQuotes + arraySlashes + 2;
            if ((long)used + arrayLength > ConfigurationBindingResolver.MaxConcatLength ||
                (long)used + arrayLength + quoted > ConfigurationBindingResolver.MaxConcatLength)
            {
                return steps;
            }

            used += arrayLength + quoted;
            length = arrayLength;
            quotes = arrayQuotes;
            slashes = arraySlashes;
        }

        return 10;
    }

    private static JToken NestedMaps(int depth)
    {
        JToken body = new JObject { ["$eval"] = "x" };
        for (var index = 0; index < depth; index++)
        {
            body = new JObject
            {
                ["$map"] = new JObject { ["$eval"] = "range(0, 50)" },
                ["each(x)"] = body,
            };
        }

        return body;
    }

    private static int LetFanOutLevels(int copies, int seedLength)
    {
        var length = seedLength + 2;
        for (var level = 1; level <= 4; level++)
        {
            length = 2 + (copies * length) + (copies - 1);
            if (length > ConfigurationBindingResolver.MaxConcatLength)
            {
                return level;
            }
        }

        return 4;
    }

    private static JToken LetFanOut(int levels, int copies, string seed)
    {
        JToken current = new JObject
        {
            ["$eval"] = RepeatedExpression($"v{levels - 1}", copies),
        };

        for (var level = levels - 1; level >= 1; level--)
        {
            current = new JObject
            {
                ["$let"] = new JObject
                {
                    [$"v{level}"] = new JObject
                    {
                        ["$eval"] = RepeatedExpression($"v{level - 1}", copies),
                    },
                },
                ["in"] = current,
            };
        }

        return new JObject
        {
            ["$let"] = new JObject
            {
                ["v0"] = seed,
            },
            ["in"] = current,
        };
    }

    private static string RepeatedExpression(string name, int copies)
    {
        return "[" + string.Join(",", Enumerable.Repeat(name, copies)) + "]";
    }

    private static int ListLiteralLength(int seedLength, int terms)
    {
        var item = seedLength + 2;
        return 2 + (terms * item) + (terms - 1);
    }

    private static int CubicWidthPastStepLimit()
    {
        // 50 is the largest width these fixtures build. 600 is the reported multi-gigabyte case.
        for (var width = 1; width <= 50; width++)
        {
            var steps = (long)width + (width * width) + ((long)width * width * width);
            if (steps > ConfigurationBindingResolver.MaxEvaluationSteps)
            {
                return width;
            }
        }

        return 50;
    }

    private static JToken NestedAll(int depth, int count)
    {
        JToken rule = new JValue(true);
        for (var level = 0; level < depth; level++)
        {
            var items = new JArray();
            for (var index = 0; index < count; index++)
            {
                items.Add(index);
            }

            rule = new JObject
            {
                ["all"] = new JArray
                {
                    items,
                    rule,
                },
            };
        }

        return rule;
    }

    private static JToken NestedDeletedMaps(int depth, int count)
    {
        JToken body = new JObject
        {
            ["$if"] = "false",
            ["then"] = 1,
        };

        for (var level = 0; level < depth; level++)
        {
            var items = new JArray();
            for (var index = 0; index < count; index++)
            {
                items.Add(index);
            }

            body = new JObject
            {
                ["$map"] = items,
                ["each(x)"] = body,
            };
        }

        return body;
    }

    private static JToken NestedJsonLogicMaps(int depth, int count, string leaf)
    {
        JToken rule = leaf;
        for (var level = 0; level < depth; level++)
        {
            var items = new JArray();
            for (var index = 0; index < count; index++)
            {
                items.Add(index);
            }

            rule = new JObject
            {
                ["map"] = new JArray
                {
                    items,
                    rule,
                },
            };
        }

        return rule;
    }

    private static JObject Parse(string json)
    {
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json))
        {
            DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
        };
        return JObject.Load(reader);
    }

    private static JToken Chain(int evaluations)
    {
        JToken current = new JObject
        {
            ["$binding"] = "jlogic",
            ["$definition"] = "done",
        };

        for (var wrap = 1; wrap < evaluations; wrap++)
        {
            current = new JObject
            {
                ["$binding"] = "jsone",
                ["$definition"] = EscapeDollars(current),
            };
        }

        return current;
    }

    private static JToken FanOut(int levels)
    {
        JToken current = new JObject
        {
            ["$binding"] = "jlogic",
            ["$definition"] = "done",
        };

        for (var level = 1; level < levels; level++)
        {
            current = new JObject
            {
                ["$binding"] = "jsone",
                ["$definition"] = new JArray(EscapeDollars(current), EscapeDollars(current)),
            };
        }

        return current;
    }

    private static JToken EscapeDollars(JToken token)
    {
        switch (token)
        {
            case JObject obj:
            {
                var copy = new JObject();
                foreach (var property in obj.Properties())
                {
                    var name = property.Name.StartsWith('$') ? "$" + property.Name : property.Name;
                    copy[name] = EscapeDollars(property.Value);
                }

                return copy;
            }

            case JArray array:
            {
                var copy = new JArray();
                foreach (var item in array)
                {
                    copy.Add(EscapeDollars(item));
                }

                return copy;
            }

            default:
                return token.DeepClone();
        }
    }
}
