using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using static _42.Platform.Storyteller.Binding.Object.UnitTests.Fixtures;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// Inputs that tied up or crashed the worker during the review of PR #57 (JetBrains Air rounds 1–19 and the
/// sandboxing analysis at 3cafac1), at the reported sizes. Every entry must stop on a limit under the default limits.
/// Vectors that only attacked the removed per-operator rewriter (for example a <c>$${</c> escape in a value, or
/// rebinding an injected <c>storyteller*</c> helper) are not attacks on plain JSON-e and are not listed.
/// </summary>
internal static class AttackCorpus
{
    private static readonly IReadOnlyDictionary<string, Func<JObject>> Vectors = new Dictionary<string, Func<JObject>>
    {
        ["r01 jsone envelope copies itself twice (2^16)"] = () => new JObject { ["value"] = SelfCopyingEnvelope(16) },

        ["r02 jsone len(range(0, 2e7))"] = () => JsonE(Eval("len(range(0, 20000000))")),
        ["r02 jsone $map over range(0, 5e6)"] = () => JsonE(new JObject { ["$map"] = Eval("range(0, 5000000)"), ["each(x)"] = true }),
        ["r02 jsone 26 nested $let s+s"] = () => JsonE(DoublingLets(26)),
        ["r02 jlogic reduce cat doubling"] = () => JsonLogic(ReduceDoubling(30, Parse("""{ "cat": [{ "var": "accumulator" }, { "var": "accumulator" }] }"""), "ab")),

        ["r03 jsone $reduce [acc,acc]"] = () => JsonE(Reduce(30, Eval("[acc,acc]"))),
        ["r03 jlogic reduce [acc,acc]"] = () => JsonLogic(ReduceDoubling(30, JArray.Parse("""[{ "var": "accumulator" }, { "var": "accumulator" }]"""), "x")),
        ["r03 jsone $reduce $json [acc,acc]"] = () => JsonE(Reduce(14, new JObject { ["$json"] = Eval("[acc,acc]") })),
        ["r03 jsone $reduce join([acc,acc])"] = () => JsonE(Reduce(30, Eval("join([acc,acc],'')"))),
        ["r03 jsone nested $map 300^3"] = () => JsonE(NestedRangeMaps(3, 300)),

        ["r04 jsone $let fan-out x10, 7 levels"] = () => JsonE(LetFanOut(7, 10, new string('x', 100))),
        ["r04 jlogic nested map 200^3"] = () => JsonLogic(NestedLogic("map", 3, 200, "xxxxxxxxxx")),

        ["r06 jlogic nested all 600^3"] = () => JsonLogic(NestedLogic("all", 3, 600, true)),
        ["r06 jlogic nested some 600^3"] = () => JsonLogic(NestedLogic("some", 3, 600, false)),
        ["r06 jsone nested $map 200^3 with deleted bodies"] = () => JsonE(NestedDeletedMaps(3, 200)),

        ["r07 jsone 255 sibling envelopes under the old per-envelope cap"] = () => JsonE(SiblingEmitter(255, 46)),

        ["r08 jsone $if len(s+...800)"] = () => JsonE(new JObject { ["$if"] = $"len({Repeat("s", 800, "+")}) > 0", ["then"] = 1, ["else"] = 0 }, Text("s", 90_000)),
        ["r08 jsone $switch key len(s+...800)"] = () => JsonE(new JObject { ["$switch"] = new JObject { [$"len({Repeat("s", 800, "+")}) > 0"] = 1, ["$default"] = 0 } }, Text("s", 90_000)),

        ["r09 jsone interpolated key len(s+...800)"] = () => JsonE(new JObject { ["${len(" + Repeat("s", 800, "+") + ")}"] = 1 }, Text("s", 90_000)),

        ["r11 jsone $$ key interpolates len(s+...800)"] = () => JsonE(new JObject { ["$${len(" + Repeat("s", 800, "+") + ")}"] = 1 }, Text("s", 90_000)),

        ["r12 jsone root [s x4000]"] = () => JsonE(Eval("[" + Repeat("s", 4_000, ",") + "]"), Text("s", 90_000)),
        ["r12 jlogic [var s x4000]"] = () => JsonLogic(new JArray(Enumerable.Range(0, 4_000).Select(_ => Parse("""{ "var": "s" }"""))), Text("s", 90_000)),
        ["r12 jsone nested [s x12000]"] = () => JsonE(new JObject { ["a"] = Eval("[" + Repeat("s", 12_000, ",") + "]") }, Text("s", 90_000)),

        ["r13 jsone 1 in xs x2000"] = () => JsonE(Eval(Repeat("1 in xs", 2_000, " || ")), Zeros("xs", 50_000)),
        ["r13 jlogic in xs x2000"] = () => JsonLogic(Or(2_000, """{ "in": [1, { "var": "xs" }] }"""), Zeros("xs", 50_000)),
        ["r13 jsone xs == ys x2000"] = () => JsonE(Eval(Repeat("xs == ys", 2_000, " && ")), Zeros("xs", 50_000, "ys")),
        ["r13 jsone len(split(s,'')) x500"] = () => JsonE(Eval(Repeat("len(split(s,'')) > 0", 500, " && ")), Text("s", 90_000)),
        ["r13 jsone nested $map 290^2 with 1 in xs"] = () => JsonE(NestedMaps(2, 290, new JObject { ["$if"] = "1 in xs", ["then"] = 1 }), Zeros("xs", 50_000)),

        ["r14 jsone s[400000]<'a' x20000"] = () => JsonE(Eval(Repeat("s[400000]<'a'", 20_000, "||")), Text("s", 500_000)),
        ["r14 jsone s[0]<'a' x2000"] = () => JsonE(Eval(Repeat("s[0]<'a'", 2_000, "||")), Text("s", 500_000)),
        ["r14 jsone len(s)<0 x5000"] = () => JsonE(Eval(Repeat("len(s)<0", 5_000, "||")), Text("s", 500_000)),
        ["r14 jlogic !== xs ys x4000"] = () => JsonLogic(Or(4_000, """{ "!==": [{ "var": "xs" }, { "var": "ys" }] }"""), Zeros("xs", 200_000, "ys")),
        ["r14 jlogic < 0 d x20000"] = () => JsonLogic(Or(20_000, """{ "<": [0, { "var": "d" }] }"""), Text("d", 500_000, '0')),
        ["r14 jlogic + d x20000"] = () => JsonLogic(Or(20_000, """{ "+": [{ "var": "d" }] }"""), Text("d", 500_000, '0')),

        ["r16 jsone nested $map 100^3 with deleted bodies"] = () => JsonE(NestedDeletedMaps(3, 100)),

        ["r18 jsone $find over 1000 in $map 300^2"] = () => JsonE(NestedMaps(2, 300, new JObject { ["$find"] = Sequence(1_000), ["each(z)"] = "z == -1" })),
        ["r18 jsone $if false||...x500 in $map 300^2"] = () => JsonE(NestedMaps(2, 300, new JObject { ["$if"] = Repeat("false", 500, "||"), ["then"] = 1 })),
        ["r18 jlogic filter 300^2 with missing 1000 keys"] = () => JsonLogic(NestedLogic("filter", 2, 300, Parse($$"""{ "!": [{ "missing": {{Keys(1_000)}} }] }"""))),

        ["r19 jsone $sort by x*1...x15000 over 5000"] = () => JsonE(new JObject { ["$sort"] = Sequence(5_000), ["by(x)"] = "x" + string.Concat(Enumerable.Repeat("*1", 15_000)) }),
        ["r19 jsone number(s) in $map 300^2"] = () => JsonE(NestedMaps(2, 300, new JObject { ["$if"] = "number(s) < 0", ["then"] = 1 }), new JObject { ["s"] = new string('0', 399_999) + "1" }),
        ["r19 jlogic missing 100k keys x200"] = () => JsonLogic(Or(200, """{ "!": [{ "missing": { "var": "keys" } }] }"""), KeysContext(100_000)),
        ["r19 jlogic missing_some 100k keys x200"] = () => JsonLogic(Or(200, """{ "!": [{ "missing_some": [1, { "var": "keys" }] }] }"""), KeysContext(100_000)),
        ["r19 jlogic var 400KB path x2000"] = () => JsonLogic(Or(2_000, """{ "var": { "var": "p" } }"""), new JObject { ["p"] = Repeat("a", 200_000, ".") }),

        // Shared string instances: N references cost N small nodes, but flattening them costs N x |s| in one primitive.
        ["shared jsone len(join([s x4000]))"] = () => JsonE(Eval("len(join([" + Repeat("s", 4_000, ",") + "],''))"), Text("s", 100_000)),
        ["shared jsone $let $json [s x4000]"] = () => JsonE(
            new JObject
            {
                ["$let"] = new JObject { ["a"] = new JObject { ["$json"] = Eval("[" + Repeat("s", 4_000, ",") + "]") } },
                ["in"] = Eval("a == ''"),
            },
            Text("s", 100_000)),
        ["shared jsone ${s} x4000 in one value"] = () => JsonE(string.Concat(Enumerable.Repeat("${s}", 4_000)), Text("s", 100_000)),
        ["shared jlogic cat [var s x4000]"] = () => JsonLogic(new JObject { ["<"] = new JArray(new JObject { ["cat"] = new JArray(Enumerable.Range(0, 4_000).Select(_ => Parse("""{ "var": "s" }"""))) }, 0) }, Text("s", 100_000)),
        ["shared jlogic merge [var xs x4000]"] = () => JsonLogic(new JObject { ["<"] = new JArray(new JObject { ["merge"] = new JArray(Enumerable.Range(0, 4_000).Select(_ => Parse("""{ "var": "xs" }"""))) }, 0) }, Zeros("xs", 100_000)),

        // Worst-case substring search: ordinal Contains filters on the first character and one other character of the
        // needle, then compares. In (ab)^n every other position passes both filters and the needle only fails on its
        // last character, so one `in` with string.Contains cost about |s| x |t| / 2 comparisons (0.6 s) and allocated nothing.
        // Both engines now search in linear time (OrdinalSearch). Each search allocates its 800 KB prefix table, so the
        // chain stops on Memory within about 100 ms; without that table it would stop on Time with a small overshoot.
        ["cpu jsone t in s x2000 (worst-case substring search)"] = () => JsonE(new JObject { ["$map"] = Eval("range(0, 2000)"), ["each(i)"] = new JObject { ["$if"] = "t in s", ["then"] = 1 } }, SearchContext()),
        ["cpu jlogic in [var t, var s] x2000 (worst-case substring search)"] = () => JsonLogic(Or(2_000, """{ "in": [{ "var": "t" }, { "var": "s" }] }"""), SearchContext()),

        ["stack jsone !x15000"] = () => JsonE(Eval(new string('!', 15_000) + "true")),
        ["stack jsone -x50000"] = () => JsonE(Eval(new string('-', 50_000) + "1")),
        ["stack jsone [x5000"] = () => JsonE(Eval(new string('[', 5_000) + "1" + new string(']', 5_000))),
        ["stack jsone [x1500 superlinear"] = () => JsonE(Eval(new string('[', 1_500) + "1" + new string(']', 1_500))),
        ["stack jsone 1+1...x50000"] = () => JsonE(Eval(Repeat("1", 50_000, "+"))),
        ["stack jlogic ! nested 5000"] = () => JsonLogic(NestedNot(5_000)),
        ["result jsone 100-deep value from $reduce"] = () => JsonE(new JObject
        {
            ["$reduce"] = Eval("range(0, 100)"),
            ["initial"] = 0,
            ["each(acc,i)"] = new JObject { ["a"] = Eval("acc") },
        }),
    };

    public static IEnumerable<object[]> Names()
    {
        return Vectors.Keys.Select(name => new object[] { name });
    }

    public static JObject Build(string name)
    {
        return Vectors[name]();
    }

    private static JObject JsonE(JToken definition, JObject? context = null)
    {
        return Envelope("jsone", definition, context);
    }

    private static JObject JsonLogic(JToken definition, JObject? context = null)
    {
        return Envelope("jlogic", definition, context);
    }

    private static JObject Eval(string expression)
    {
        return new JObject { ["$eval"] = expression };
    }

    private static JObject Text(string name, int length, char fill = 'x')
    {
        return new JObject { [name] = new string(fill, length) };
    }

    private static JObject Zeros(string name, int count, string? copyName = null)
    {
        var context = new JObject { [name] = NumberArray(count) };
        if (copyName is not null)
        {
            context[copyName] = NumberArray(count);
        }

        return context;
    }

    /// <summary>
    /// <c>s</c> = (ab)^200,000 (400,000 characters) and <c>t</c> = (ab)^100,000 + "aa" (200,002 characters): about 600 KB stored.
    /// </summary>
    private static JObject SearchContext()
    {
        return new JObject
        {
            ["s"] = string.Concat(Enumerable.Repeat("ab", 200_000)),
            ["t"] = string.Concat(Enumerable.Repeat("ab", 100_000)) + "aa",
        };
    }

    private static JArray Keys(int count)
    {
        return new JArray(Enumerable.Range(0, count).Select(index => "k" + index));
    }

    private static JObject KeysContext(int count)
    {
        return new JObject { ["keys"] = Keys(count) };
    }

    private static JObject Or(int count, string rule)
    {
        var template = Parse(rule);
        return new JObject { ["or"] = new JArray(Enumerable.Range(0, count).Select(_ => template.DeepClone())) };
    }

    private static JObject Reduce(int steps, JToken body)
    {
        return new JObject
        {
            ["$reduce"] = Eval($"range(0, {steps})"),
            ["initial"] = "x",
            ["each(acc,i)"] = body,
        };
    }

    private static JObject ReduceDoubling(int steps, JToken body, string seed)
    {
        return new JObject { ["reduce"] = new JArray { Sequence(steps), body, seed } };
    }

    private static JToken NestedRangeMaps(int depth, int count)
    {
        JToken body = Eval("x");
        for (var level = 0; level < depth; level++)
        {
            body = new JObject
            {
                ["$map"] = Eval($"range(0, {count})"),
                ["each(x)"] = body,
            };
        }

        return body;
    }

    private static JToken NestedMaps(int depth, int count, JToken innermost)
    {
        var body = innermost;
        for (var level = 0; level < depth; level++)
        {
            body = new JObject
            {
                ["$map"] = Sequence(count),
                [$"each(v{level})"] = body,
            };
        }

        return body;
    }

    private static JToken NestedDeletedMaps(int depth, int count)
    {
        return NestedMaps(depth, count, new JObject { ["$if"] = "false", ["then"] = 1 });
    }

    private static JToken NestedLogic(string rule, int depth, int count, JToken innermost)
    {
        var body = innermost;
        for (var level = 0; level < depth; level++)
        {
            body = new JObject { [rule] = new JArray { Sequence(count), body } };
        }

        return body;
    }

    private static JToken NestedNot(int depth)
    {
        JToken rule = true;
        for (var level = 0; level < depth; level++)
        {
            rule = new JObject { ["!"] = new JArray { rule } };
        }

        return rule;
    }

    private static JToken LetFanOut(int levels, int copies, string seed)
    {
        JToken current = Eval("[" + Repeat($"v{levels - 1}", copies, ",") + "]");
        for (var level = levels - 1; level >= 1; level--)
        {
            current = new JObject
            {
                ["$let"] = new JObject { [$"v{level}"] = Eval("[" + Repeat($"v{level - 1}", copies, ",") + "]") },
                ["in"] = current,
            };
        }

        return new JObject
        {
            ["$let"] = new JObject { ["v0"] = seed },
            ["in"] = current,
        };
    }

    /// <summary>
    /// One envelope that emits <paramref name="siblings"/> envelopes, each running a cubic loop of <paramref name="width"/>.
    /// </summary>
    private static JToken SiblingEmitter(int siblings, int width)
    {
        var inner = new JObject
        {
            ["$binding"] = "jsone",
            ["$definition"] = new JObject
            {
                ["$let"] = new JObject { ["xs"] = NumberArray(width) },
                ["in"] = new JObject
                {
                    ["$map"] = Eval("xs"),
                    ["each(x)"] = new JObject
                    {
                        ["$map"] = Eval("xs"),
                        ["each(y)"] = new JObject
                        {
                            ["$map"] = NumberArray(width),
                            ["each(z)"] = new JObject { ["$if"] = "false", ["then"] = 1 },
                        },
                    },
                },
            },
        };

        return new JObject
        {
            ["$map"] = Eval($"range(0, {siblings})"),
            ["each(i)"] = EscapeDollars(inner),
        };
    }

    /// <summary>
    /// An envelope whose definition, kept in <c>$context.d</c>, emits two copies of itself while <c>n</c> is positive.
    /// </summary>
    private static JToken SelfCopyingEnvelope(int levels)
    {
        var copy = new JObject
        {
            ["$$binding"] = "jsone",
            ["$$definition"] = Eval("d"),
            ["$$context"] = new JObject
            {
                ["d"] = Eval("d"),
                ["n"] = Eval("n - 1"),
            },
        };
        var definition = new JObject
        {
            ["$if"] = "n > 0",
            ["then"] = new JArray(copy, copy.DeepClone()),
            ["else"] = "done",
        };

        return new JObject
        {
            ["$binding"] = "jsone",
            ["$definition"] = definition,
            ["$context"] = new JObject
            {
                ["d"] = definition.DeepClone(),
                ["n"] = levels,
            },
        };
    }
}
