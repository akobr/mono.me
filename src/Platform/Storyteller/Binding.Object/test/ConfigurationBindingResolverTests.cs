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
