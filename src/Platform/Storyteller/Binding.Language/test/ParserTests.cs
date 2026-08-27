using System.Collections.Generic;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class ParserTests
{
    [Fact]
    public void Parse_Path_ProducesPathStatement()
    {
        var node = Parse("@store.book.title");

        node.Should().BeOfType<PathStatement>()
            .Which.Path.Segments.Should().Equal("store", "book", "title");
    }

    [Fact]
    public void Parse_SingleIdentifier_ProducesOneSegmentPath()
    {
        Parse("@value").Should().BeOfType<PathStatement>()
            .Which.Path.Segments.Should().Equal("value");
    }

    [Fact]
    public void Parse_Sourced_ProducesSourcedStatement()
    {
        var sourced = Parse("@(a.b, vault)").Should().BeOfType<SourcedStatement>().Subject;

        sourced.Path.Segments.Should().Equal("a", "b");
        sourced.Source.Should().Be("vault");
    }

    [Fact]
    public void Parse_FunctionWithoutArguments_ProducesEmptyArguments()
    {
        var function = Parse("@now()").Should().BeOfType<FunctionStatement>().Subject;

        function.Name.Should().Be("now");
        function.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_FunctionWithStringLiteral_ProducesStringLiteralArgument()
    {
        var function = Parse("@path(\"$.store.book[0]\")").Should().BeOfType<FunctionStatement>().Subject;

        function.Name.Should().Be("path");
        function.Arguments.Should().ContainSingle()
            .Which.Should().BeOfType<StringLiteralNode>()
            .Which.Value.Should().Be("$.store.book[0]");
    }

    [Fact]
    public void Parse_FunctionWithMixedArguments_ProducesTypedArguments()
    {
        var function = Parse("@coalesce(@a.b, c.d, \"x\")").Should().BeOfType<FunctionStatement>().Subject;

        function.Name.Should().Be("coalesce");
        function.Arguments.Should().HaveCount(3);

        function.Arguments[0].Should().BeOfType<PathStatement>()
            .Which.Path.Segments.Should().Equal("a", "b");
        function.Arguments[1].Should().BeOfType<PathNode>()
            .Which.Segments.Should().Equal("c", "d");
        function.Arguments[2].Should().BeOfType<StringLiteralNode>()
            .Which.Value.Should().Be("x");
    }

    [Fact]
    public void Parse_NestedFunctionArgument_ProducesNestedStatement()
    {
        var outer = Parse("@upper(@inner(x))").Should().BeOfType<FunctionStatement>().Subject;

        var inner = outer.Arguments.Should().ContainSingle()
            .Which.Should().BeOfType<FunctionStatement>().Subject;
        inner.Name.Should().Be("inner");
        inner.Arguments.Should().ContainSingle()
            .Which.Should().BeOfType<PathNode>()
            .Which.Segments.Should().Equal("x");
    }

    [Fact]
    public void Parse_Interpolation_ProducesTextAndStatementParts()
    {
        var interpolation = Parse("@[Hello @user.name!]").Should().BeOfType<InterpolationExpression>().Subject;

        interpolation.Parts.Should().HaveCount(3);
        interpolation.Parts[0].Should().BeOfType<TextPart>().Which.Text.Should().Be("Hello ");
        interpolation.Parts[1].Should().BeOfType<StatementPart>()
            .Which.Statement.Should().BeOfType<PathStatement>()
            .Which.Path.Segments.Should().Equal("user", "name");
        interpolation.Parts[2].Should().BeOfType<TextPart>().Which.Text.Should().Be("!");
    }

    [Fact]
    public void Parse_MathPrecedence_BindsMultiplicationBeforeAddition()
    {
        var math = Parse("@{1 + 2 * 3}").Should().BeOfType<MathExpression>().Subject;

        var root = math.Expression.Should().BeOfType<BinaryNode>().Subject;
        root.Operator.Should().Be('+');
        root.Left.Should().BeOfType<NumberNode>().Which.Value.Should().Be(1m);

        var product = root.Right.Should().BeOfType<BinaryNode>().Subject;
        product.Operator.Should().Be('*');
        product.Left.Should().BeOfType<NumberNode>().Which.Value.Should().Be(2m);
        product.Right.Should().BeOfType<NumberNode>().Which.Value.Should().Be(3m);
    }

    [Fact]
    public void Parse_MathSubtraction_IsLeftAssociative()
    {
        var math = Parse("@{1 - 2 - 3}").Should().BeOfType<MathExpression>().Subject;

        var root = math.Expression.Should().BeOfType<BinaryNode>().Subject;
        root.Operator.Should().Be('-');
        root.Right.Should().BeOfType<NumberNode>().Which.Value.Should().Be(3m);

        var left = root.Left.Should().BeOfType<BinaryNode>().Subject;
        left.Operator.Should().Be('-');
        left.Left.Should().BeOfType<NumberNode>().Which.Value.Should().Be(1m);
        left.Right.Should().BeOfType<NumberNode>().Which.Value.Should().Be(2m);
    }

    [Fact]
    public void Parse_MathGrouping_OverridesPrecedence()
    {
        var math = Parse("@{(1 + 2) * 3}").Should().BeOfType<MathExpression>().Subject;

        var root = math.Expression.Should().BeOfType<BinaryNode>().Subject;
        root.Operator.Should().Be('*');
        root.Right.Should().BeOfType<NumberNode>().Which.Value.Should().Be(3m);

        var sum = root.Left.Should().BeOfType<BinaryNode>().Subject;
        sum.Operator.Should().Be('+');
        sum.Left.Should().BeOfType<NumberNode>().Which.Value.Should().Be(1m);
        sum.Right.Should().BeOfType<NumberNode>().Which.Value.Should().Be(2m);
    }

    [Fact]
    public void Parse_MathWithStatementOperand_ProducesStatementOperand()
    {
        var math = Parse("@{@a.b + 1}").Should().BeOfType<MathExpression>().Subject;

        var root = math.Expression.Should().BeOfType<BinaryNode>().Subject;
        root.Left.Should().BeOfType<StatementOperand>()
            .Which.Statement.Should().BeOfType<PathStatement>()
            .Which.Path.Segments.Should().Equal("a", "b");
        root.Right.Should().BeOfType<NumberNode>().Which.Value.Should().Be(1m);
    }

    [Fact]
    public void Parse_MissingCommaInSourced_ThrowsAtSourcePosition()
    {
        var act = () => Parse("@(a.b src)");

        act.Should().Throw<BindingSyntaxException>().Which.Position.Should().Be(6);
    }

    [Theory]
    [InlineData("@f(g(x))")]
    [InlineData("@{1 + }")]
    [InlineData("@{1 2}")]
    public void Parse_InvalidGrammar_Throws(string source)
    {
        var act = () => Parse(source);

        act.Should().Throw<BindingSyntaxException>();
    }

    private static BindingNode Parse(string source)
    {
        var tokens = new Tokenizer(source).Tokenize();
        return new Parser(tokens).Parse();
    }
}
