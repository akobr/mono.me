using System;
using System.Collections.Generic;
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
}
