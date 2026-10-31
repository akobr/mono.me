using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using static _42.Platform.Storyteller.Binding.Object.UnitTests.Fixtures;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// A value of <c>$context</c> can be a secret, so no engine message that leaves the resolver may quote one.
/// Each case reaches one of the vendored JSON-e messages that print an evaluated value; if the fork's text changes,
/// the case fails instead of letting the value through.
/// </summary>
public class EngineMessagesTests
{
    private const string Secret = "SECRET_SENTINEL";

    private readonly ConfigurationBindingResolver _resolver = new(new FakeStringBinding());
    private readonly BindingScope _scope = new();

    [Fact]
    public async Task JsonE_NumberCalledAsFunction_DoesNotQuoteTheNumber()
    {
        var thrown = await ResolveThrows("jsone", Eval("pin()"), new JObject { ["pin"] = 987654321 });

        thrown.Message.Should().Be("Failed to process the object binding for 'value': A called value is not a function.");
        thrown.InnerException!.InnerException!.Message.Should().Contain("987654321", "the fork's own message quotes the value");
    }

    [Fact]
    public async Task JsonE_StringCalledAsFunction_DoesNotQuoteTheString()
    {
        var thrown = await ResolveThrows("jsone", Eval("pin()"), new JObject { ["pin"] = Secret });

        thrown.Message.Should().Be("Failed to process the object binding for 'value': unknown context value");
        thrown.InnerException!.InnerException!.Message.Should().Be($"unknown context value {Secret}");
    }

    [Fact]
    public async Task JsonE_StringThatIsNoNameCalledAsFunction_DoesNotQuoteTheString()
    {
        var thrown = await ResolveThrows("jsone", Eval("pin()"), new JObject { ["pin"] = "-" + Secret });

        thrown.Message.Should().Be("Failed to process the object binding for 'value': A called name is not a valid context accessor.");
        thrown.InnerException!.InnerException!.Message.Should().Be($"-{Secret} is not a valid accessor");
    }

    [Fact]
    public async Task JsonE_DefinedOfAnInvalidAccessor_DoesNotQuoteTheArgument()
    {
        var thrown = await ResolveThrows("jsone", Eval("defined(pin)"), new JObject { ["pin"] = "-" + Secret });

        thrown.Message.Should().Be("Failed to process the object binding for 'value': The argument of defined() is not a valid context accessor.");
        thrown.InnerException!.InnerException!.Message.Should().Be($"'-{Secret}' is not a valid context accessor");
    }

    [Fact]
    public async Task JsonE_StringCalledAsFunction_KeepsNoPartOfAWordOfTheDefinition()
    {
        // "p" occurs in "pin()" but is not a whole word of the definition.
        var thrown = await ResolveThrows("jsone", Eval("pin()"), new JObject { ["pin"] = "p" });

        thrown.Message.Should().EndWith(": unknown context value");
    }

    [Theory]
    [InlineData("missingName", "unknown context value missingName")]
    [InlineData("present.missingName", "object has no property \"missingName\"")]
    public async Task JsonE_UnknownAuthoredName_IsKept(string expression, string message)
    {
        var thrown = await ResolveThrows("jsone", Eval(expression), new JObject { ["present"] = new JObject { ["x"] = Secret } });

        thrown.Message.Should().Be($"Failed to process the object binding for 'value': {message}");
    }

    [Fact]
    public async Task JsonE_EngineMessageWithoutValue_IsForwarded()
    {
        var thrown = await ResolveThrows("jsone", Eval("'ab'[5]"), new JObject());

        thrown.Message.Should().Contain("index out of bounds");
    }

    [Theory]
    [MemberData(nameof(JsonLogicFailures))]
    public async Task JsonLogic_Failure_HasAFixedMessage(string rule)
    {
        var thrown = await ResolveThrows("jlogic", Parse(rule), new JObject { ["s"] = Secret, ["o"] = new JObject { ["k"] = Secret } });

        thrown.Message.Should().Be("Failed to process the object binding for 'value': JSON Logic evaluation failed.");
        thrown.InnerException!.InnerException.Should().BeAssignableTo<Json.Logic.JsonLogicException>();
    }

    [Fact]
    public async Task UnexpectedEngineException_IsNamedByItsTypeOnly()
    {
        var thrown = await ResolveThrows("jsone", Eval("2 ** 100000"), new JObject());

        thrown.Message.Should().Be("Failed to process the object binding for 'value': Object binding evaluation failed (OverflowException).");
    }

    [Fact]
    public async Task JsonE_LongStringCalledAsFunction_IsCheckedInLinearTime()
    {
        // The name is not a word of the definition, but it matches at every offset of the padding.
        var definition = new JObject
        {
            ["$let"] = new JObject { ["p"] = new string('a', 600_000) },
            ["in"] = Eval(new string('a', 300_000)),
        };
        var watch = Stopwatch.StartNew();

        var thrown = await ResolveThrows("jsone", definition, new JObject());

        thrown.Message.Should().EndWith(": unknown context value");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [MemberData(nameof(EmittedEnvelopes))]
    public async Task EmittedEnvelope_ComputedFromASecret_IsReportedAtTheStoredEnvelope(string emitted, string message)
    {
        // The outer JSON-e emits an envelope ($$ escapes $) built from a $context value, and the resolver evaluates it too.
        var thrown = await ResolveThrows("jsone", Parse(emitted), new JObject { ["s"] = Secret });

        thrown.Message.Should().Be($"Failed to process the object binding for 'value': {message}");
    }

    [Theory]
    [InlineData("@fail", false)]
    [InlineData("@deep", true)]
    public async Task EmittedDataBinding_UnderAComputedKey_IsReportedAtTheStoredEnvelope(string binding, bool limit)
    {
        var resolver = new ConfigurationBindingResolver(new FailingStringBinding());
        var content = Envelope("jsone", new JObject { ["${s}"] = binding }, new JObject { ["s"] = Secret });

        var act = () => resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        var thrown = (await act.Should().ThrowAsync<BindingEvaluationException>()).Which;
        thrown.Path.Should().Be("value");
        thrown.Message.Should().NotContain(Secret).And.StartWith("Failed to process the object binding for 'value': a data binding in its result");
        thrown.InnerException!.Message.Should().Contain(Secret, "the executor's own message names the computed path");
        if (limit)
        {
            var exceeded = thrown.Should().BeOfType<EvaluationLimitExceededException>().Subject;
            exceeded.Kind.Should().Be(EvaluationLimitKind.Depth);
            exceeded.Limit.Should().Be(64);
        }
    }

    [Fact]
    public async Task StoredDataBinding_KeepsItsPathAndMessage()
    {
        var resolver = new ConfigurationBindingResolver(new FailingStringBinding());
        var content = Parse("""{ "settings": { "value": "@fail" } }""");

        var act = () => resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        var thrown = (await act.Should().ThrowAsync<BindingEvaluationException>()).Which;
        thrown.Path.Should().Be("settings.value");
        thrown.Message.Should().Be("Failed to process the data binding for 'settings.value': it failed.");
    }

    public static IEnumerable<object[]> EmittedEnvelopes()
    {
        yield return ["""{ "$$binding": "${s}", "$$definition": 1 }""", "Unknown object binding: $binding must be 'jsone' or 'jlogic'."];
        yield return ["""{ "$$binding": "jsone", "$$definition": 1, "${s}": 1 }""", "An object binding allows only $binding, $definition and $context."];
        yield return ["""{ "$$binding": "jsone", "$$definition": { "$$eval": "${s}" } }""", "JSON-e evaluation failed."];
        yield return ["""{ "$$binding": "jsone", "$$context": { "o": {} }, "$$definition": { "$$eval": "o.${s}" } }""", "JSON-e evaluation failed."];
    }

    public static IEnumerable<object[]> JsonLogicFailures()
    {
        yield return ["""{ "cat": [{ "var": "o" }] }"""];
        yield return ["""{ "in": [{ "var": "o" }, { "var": "s" }] }"""];
    }

    private static JObject Eval(string expression)
    {
        return new JObject { ["$eval"] = expression };
    }

    private async Task<BindingEvaluationException> ResolveThrows(string kind, JToken definition, JObject context)
    {
        var content = Envelope(kind, definition, context);

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        var thrown = (await act.Should().ThrowAsync<BindingEvaluationException>()).Which;
        thrown.Path.Should().Be("value");
        thrown.Message.Should().NotContain(Secret);
        return thrown;
    }

    // Fails like the real executor: the message names the JSON path of the string, which is computed inside a result.
    private sealed class FailingStringBinding : IBindingExecutor
    {
        public ValueTask<bool> TryBinding(JProperty property, bool includeSecrets, BindingScope? scope = null)
        {
            return Fail(property.Value);
        }

        public ValueTask<bool> TryBinding(JValue value, bool includeSecrets, BindingScope? scope = null)
        {
            return Fail(value);
        }

        private static ValueTask<bool> Fail(JToken current)
        {
            var path = current.Path;
            throw current.Value<string>() switch
            {
                "@fail" => new BindingEvaluationException($"Failed to process the data binding for '{path}': it failed.", path, null),
                "@deep" => new EvaluationLimitExceededException(EvaluationLimitKind.Depth, 64, $"Failed to process the data binding for '{path}': too deep.", path, null),
                _ => new InvalidOperationException("Unexpected binding."),
            };
        }
    }
}
