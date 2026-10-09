using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using static _42.Platform.Storyteller.Binding.Object.UnitTests.Fixtures;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// Envelope semantics and plain JSON Logic / JSON-e behavior under the default limits.
/// Limits are covered by <see cref="EvaluationLimitTests"/> and <see cref="AttackCorpusTests"/>.
/// </summary>
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

        var thrown = await act.Should().ThrowAsync<BindingEvaluationException>();
        thrown.Which.Path.Should().Be("value");
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

        await act.Should().ThrowAsync<BindingEvaluationException>();
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
    public async Task Resolve_EnvelopeChainOf33_ThrowsEnvelopeDepth()
    {
        var content = new JObject { ["value"] = Chain(33) };

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        var thrown = await act.Should().ThrowAsync<EvaluationLimitExceededException>();
        thrown.Which.Kind.Should().Be(EvaluationLimitKind.EnvelopeDepth);
        thrown.Which.Limit.Should().Be(ObjectBindingLimits.Default.MaxEnvelopeDepth);
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
    public async Task Resolve_EnvelopeFanOutPastEvaluationBudget_ThrowsEnvelopeCount()
    {
        var limit = ObjectBindingLimits.Default.MaxEnvelopeEvaluations;
        var levels = 1;
        while ((1 << levels) - 1 <= limit)
        {
            levels++;
        }

        var content = new JObject { ["value"] = FanOut(levels) };
        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        var thrown = await act.Should().ThrowAsync<EvaluationLimitExceededException>()
            .WithMessage($"*exceed {limit} evaluations*");
        thrown.Which.Kind.Should().Be(EvaluationLimitKind.EnvelopeCount);
    }

    [Fact]
    public async Task Resolve_EngineError_KeepsEvaluationTypeAndPath()
    {
        var content = Envelope("jsone", new JObject { ["$eval"] = "unknown" });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        var thrown = await act.Should().ThrowAsync<BindingEvaluationException>()
            .WithMessage("Failed to process the object binding for 'value':*");
        thrown.Which.Path.Should().Be("value");
        thrown.Which.Should().NotBeOfType<EvaluationLimitExceededException>();
    }

    [Fact]
    public async Task Resolve_UnexpectedEngineException_IsBindingEvaluationException()
    {
        // Math.Pow overflows decimal inside JsonE.Net; the raw OverflowException must not escape as a server fault.
        var content = Envelope("jsone", new JObject { ["$eval"] = "2 ** 100000" });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        var thrown = await act.Should().ThrowAsync<BindingEvaluationException>();
        thrown.Which.InnerException.Should().BeOfType<BindingEvaluationException>()
            .Which.InnerException.Should().BeOfType<OverflowException>();
    }

    [Fact]
    public async Task Resolve_JsonLogicCat_ConcatenatesStrings()
    {
        var content = Envelope("jlogic", Parse("""{ "cat": ["a", "b"] }"""));

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("ab");
    }

    [Fact]
    public async Task Resolve_JsonERange_ReturnsLength()
    {
        var content = Envelope("jsone", new JObject { ["$eval"] = "len(range(0, 1000))" });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(1000);
    }

    [Fact]
    public async Task Resolve_JsonENestedLet_DoublesString()
    {
        var content = Envelope("jsone", DoublingLets(3));

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<string>().Should().Be("aaaaaaaa");
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
    public async Task Resolve_JsonEJoin_JoinsStrings()
    {
        var content = Envelope("jsone", new JObject { ["$eval"] = "join(['a','b'],'-')" });

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
        var content = Envelope("jsone", new JObject { ["$json"] = new JArray("x", "x") });

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
    public async Task Resolve_JsonEFind_ReturnsFirstMatch()
    {
        var plus = Envelope("jsone", new JObject
        {
            ["$find"] = new JArray("aa", "b"),
            ["each(x)"] = "len(x+x) > 3",
        });
        await _resolver.ResolveAsync(plus, includeSecrets: true, _scope);
        plus["value"]!.Value<string>().Should().Be("aa");

        var found = Envelope("jsone", new JObject
        {
            ["$find"] = new JArray(1, 2, 3),
            ["each(x)"] = "x == 2",
        });
        await _resolver.ResolveAsync(found, includeSecrets: true, _scope);
        found["value"]!.Value<decimal>().Should().Be(2);
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
        // JsonE.Net 3.0.1 replaces hole text with string.Replace, so the same hole written both escaped and live
        // ("$${x} and ${x}") renders wrongly; see Binding.JsonE parity tests. Distinct holes render correctly.
        var content = Envelope(
            "jsone",
            new JObject
            {
                ["mixed"] = "$${y} and ${x}",
                ["nested"] = "$${a ${s}}",
            },
            new JObject
            {
                ["x"] = 2,
                ["s"] = "ab",
            });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        var value = content["value"].Should().BeOfType<JObject>().Subject;
        value["mixed"]!.Value<string>().Should().Be("${y} and 2");
        value["nested"]!.Value<string>().Should().Be("${a ab}");
    }

    [Fact]
    public async Task Resolve_JsonEInterpolatedKey_KeepsEscapedDollar()
    {
        var mixed = Envelope(
            "jsone",
            new JObject { ["pre$${y} and ${x}"] = 1 },
            new JObject { ["x"] = 2 });
        await _resolver.ResolveAsync(mixed, includeSecrets: true, _scope);
        mixed["value"].Should().BeOfType<JObject>().Which["pre${y} and 2"]!.Value<decimal>().Should().Be(1);

        // JSON-e removes one '$' from a '$$' key before it interpolates the key.
        var onlyHole = Envelope(
            "jsone",
            new JObject { ["$${x}"] = 1 },
            new JObject { ["x"] = 2 });
        await _resolver.ResolveAsync(onlyHole, includeSecrets: true, _scope);
        onlyHole["value"].Should().BeOfType<JObject>().Which["2"]!.Value<decimal>().Should().Be(1);
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
        var content = Envelope("jsone", "${name[0:1] + '.'}", new JObject { ["name"] = "ab" });

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
    public async Task Resolve_JsonELet_ComputedAndLargeValues()
    {
        var computed = Envelope(
            "jsone",
            new JObject
            {
                ["$let"] = new JObject { ["$eval"] = "{a: 1}" },
                ["in"] = new JObject { ["$eval"] = "a" },
            });
        await _resolver.ResolveAsync(computed, includeSecrets: true, _scope);
        computed["value"]!.Value<decimal>().Should().Be(1);

        var text = new string('a', 40_000);
        var large = Envelope(
            "jsone",
            new JObject
            {
                ["$let"] = new JObject { ["big"] = new JObject { ["$eval"] = "s" } },
                ["in"] = new JObject { ["k"] = new JObject { ["$eval"] = "big" } },
            },
            new JObject { ["s"] = text });
        await _resolver.ResolveAsync(large, includeSecrets: true, _scope);
        large["value"]!["k"]!.Value<string>().Should().Be(text);
    }

    [Fact]
    public async Task Resolve_JsonLogicLoops_IterateLiterals()
    {
        var mapped = Envelope("jlogic", Parse("""{ "map": [[1, 2], "a"] }"""));
        await _resolver.ResolveAsync(mapped, includeSecrets: true, _scope);
        mapped["value"].Should().BeOfType<JArray>().Which.Select(item => item!.Value<string>()).Should().Equal("a", "a");

        var all = Envelope("jlogic", Parse("""{ "all": [[1, 2], true] }"""));
        await _resolver.ResolveAsync(all, includeSecrets: true, _scope);
        all["value"]!.Value<bool>().Should().BeTrue();

        var filtered = Envelope("jlogic", Parse("""{ "filter": [[1, 2], { ">": [{ "var": "" }, 1] }] }"""));
        await _resolver.ResolveAsync(filtered, includeSecrets: true, _scope);
        filtered["value"].Should().BeOfType<JArray>().Which.Select(item => item!.Value<decimal>()).Should().Equal(2m);
    }

    [Theory]
    [InlineData("'foo' in 'foobar'", "true")]
    [InlineData("'a' in ['b', 'a']", "true")]
    [InlineData("'foo' in {foo: 1}", "true")]
    [InlineData("'ab' == 'ab'", "true")]
    [InlineData("[1, 2] == [1, 2]", "true")]
    [InlineData("'a' != 'b'", "true")]
    [InlineData("split('left:right', ':')", """["left","right"]""")]
    [InlineData("split('ab', '')", """["a","b"]""")]
    [InlineData("split('a0b', 0)", """["a","b"]""")]
    [InlineData("lowercase('Fools!')", "\"fools!\"")]
    [InlineData("uppercase('Fools!')", "\"FOOLS!\"")]
    [InlineData("strip('  room  ')", "\"room\"")]
    [InlineData("lstrip('  room  ')", "\"room  \"")]
    [InlineData("rstrip('  room  ')", "\"  room\"")]
    [InlineData("[len('abc'), len('😀'), len([1, 2, 3])]", "[3,1,3]")]
    [InlineData("['abc'[0], 'abc'[-1], '😀'[0], {a: 1}['a'], [10, 20, 30][0], [10, 20, 30][-1]]", """["a","c","😀",1,10,30]""")]
    [InlineData("{a: 1}['missing']", "null")]
    [InlineData("['abcde'[1:3], 'abcde'[:2], 'abcde'[2:], 'abcde'[-2:], 'abcde'[4:2], 'abcde'[:-1]]", """["bc","ab","cde","de","","abcd"]""")]
    public async Task Resolve_JsonEBuiltins_BehaveLikeJsonE(string expression, string expected)
    {
        var content = Envelope("jsone", new JObject { ["$eval"] = expression });

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.ToString(Newtonsoft.Json.Formatting.None).Should().Be(expected);
    }

    [Fact]
    public async Task Resolve_JsonEIndexOutOfBounds_IsEvaluationError()
    {
        var content = Envelope("jsone", new JObject { ["$eval"] = "'ab'[5]" });

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>().WithMessage("*index out of bounds*");
    }

    [Theory]
    [InlineData("""{ "in": ["foo", "foobar"] }""", "true")]
    [InlineData("""{ "in": ["a", ["b", "a"]] }""", "true")]
    [InlineData("""{ "==": [[1, 2], [1, 2]] }""", "true")]
    [InlineData("""{ "!=": [[1], [2]] }""", "true")]
    [InlineData("""{ "===": [[1, 1], [1, 1]] }""", "true")]
    [InlineData("""{ "===": [1, "1"] }""", "false")]
    [InlineData("""{ "!==": [[1], [2]] }""", "true")]
    [InlineData("""{ "!==": [{ "a": 1 }, { "a": 2 }] }""", "true")]
    [InlineData("""{ "<": [1, 2, 3] }""", "true")]
    [InlineData("""{ "<": [[1], 0] }""", "false")]
    [InlineData("""{ "<=": [1, 1] }""", "true")]
    [InlineData("""{ ">": [1, 0] }""", "true")]
    [InlineData("""{ ">=": [1, 1] }""", "true")]
    [InlineData("""{ "+": [1, 2] }""", "3")]
    [InlineData("""{ "-": [5, 2] }""", "3")]
    [InlineData("""{ "*": [3, 4] }""", "12")]
    [InlineData("""{ "/": [6, 2] }""", "3")]
    [InlineData("""{ "%": [5, 2] }""", "1")]
    [InlineData("""{ "min": [1, 3] }""", "1")]
    [InlineData("""{ "max": [1, 3] }""", "3")]
    [InlineData("""{ "log": 1 }""", "1")]
    public async Task Resolve_JsonLogicRules_BehaveLikeJsonLogic(string rule, string expected)
    {
        var content = Envelope("jlogic", Parse(rule));

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.ToString(Newtonsoft.Json.Formatting.None).Should().Be(expected);
    }

    [Fact]
    public async Task Resolve_JsonLogicLog_DoesNotWriteWhenDebugIsOff()
    {
        var logger = new CapturingLogger(Microsoft.Extensions.Logging.LogLevel.Information);
        var resolver = new ConfigurationBindingResolver(_strings, logger);
        var content = Envelope("jlogic", Parse("""{ "log": { "var": "xs" } }"""), new JObject { ["xs"] = NumberArray(3) });

        await resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"].Should().BeOfType<JArray>().Which.Should().HaveCount(3);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_LargeContextData_StaysWithinDefaultLimits()
    {
        // Single reads of large context values are linear in the stored data and must not trip a limit.
        var xs = NumberArray(100_001);
        var text = new string('a', 100_001);
        var content = new JObject
        {
            ["length"] = EnvelopeToken("jsone", new JObject { ["$eval"] = "len(xs)" }, new JObject { ["xs"] = xs }),
            ["first"] = EnvelopeToken("jsone", new JObject { ["$eval"] = "xs[0]" }, new JObject { ["xs"] = xs.DeepClone() }),
            ["slice"] = EnvelopeToken("jsone", new JObject { ["$eval"] = "len(s[1:2])" }, new JObject { ["s"] = text }),
            ["strict"] = EnvelopeToken("jlogic", Parse("""{ "===": [{ "var": "s" }, 1] }"""), new JObject { ["s"] = text }),
            ["member"] = EnvelopeToken("jlogic", Parse("""{ "in": [1, { "var": "xs" }] }"""), new JObject { ["xs"] = xs.DeepClone() }),
        };

        await _resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["length"]!.Value<decimal>().Should().Be(100_001);
        content["first"]!.Value<decimal>().Should().Be(0);
        content["slice"]!.Value<decimal>().Should().Be(1);
        content["strict"]!.Value<bool>().Should().BeFalse();
        content["member"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_ContextString_IsReadManyTimes()
    {
        const int reads = 64;
        var text = new string('b', 50_000);
        var jsone = Envelope(
            "jsone",
            new JObject { ["$eval"] = Repeat("s<'a'", reads, "||") },
            new JObject { ["s"] = text });
        await _resolver.ResolveAsync(jsone, includeSecrets: true, _scope);
        jsone["value"]!.Value<bool>().Should().BeFalse();

        var comparisons = new JArray();
        for (var index = 0; index < reads; index++)
        {
            comparisons.Add(Parse("""{ "==": [{ "var": "s" }, "a"] }"""));
        }

        var logic = Envelope("jlogic", new JObject { ["or"] = comparisons }, new JObject { ["s"] = text });
        await _resolver.ResolveAsync(logic, includeSecrets: true, _scope);
        logic["value"]!.Value<bool>().Should().BeFalse();
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
}
