using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Xunit;
using static _42.Platform.Storyteller.Binding.Language.TokenType;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class TokenizerTests
{
    [Fact]
    public void Tokenize_Path_ProducesExactOffsets()
    {
        var tokens = Tokenize("@alpha.beta");

        tokens.Should().Equal(
            new Token(At, "@", 0, 1),
            new Token(Identifier, "alpha", 1, 5),
            new Token(Dot, ".", 6, 1),
            new Token(Identifier, "beta", 7, 4),
            new Token(Eof, string.Empty, 11, 0));
    }

    [Fact]
    public void Tokenize_SingleIdentifier_IsOneSegmentPath()
    {
        Shape(Tokenize("@x")).Should().Equal((At, "@"), (Identifier, "x"), (Eof, string.Empty));
    }

    [Fact]
    public void Tokenize_Sourced_ProducesExactOffsets()
    {
        var tokens = Tokenize("@(a.b, src)");

        tokens.Should().Equal(
            new Token(At, "@", 0, 1),
            new Token(LParen, "(", 1, 1),
            new Token(Identifier, "a", 2, 1),
            new Token(Dot, ".", 3, 1),
            new Token(Identifier, "b", 4, 1),
            new Token(Comma, ",", 5, 1),
            new Token(Identifier, "src", 7, 3),
            new Token(RParen, ")", 10, 1),
            new Token(Eof, string.Empty, 11, 0));
    }

    [Fact]
    public void Tokenize_FunctionWithoutArguments_ProducesTokens()
    {
        Shape(Tokenize("@now()")).Should().Equal(
            (At, "@"), (Identifier, "now"), (LParen, "("), (RParen, ")"), (Eof, string.Empty));
    }

    [Fact]
    public void Tokenize_FunctionWithStringLiteral_DecodesLiteralWithoutQuotes()
    {
        var tokens = Tokenize("@path(\"$.store.book[0]\")");

        tokens.Should().Equal(
            new Token(At, "@", 0, 1),
            new Token(Identifier, "path", 1, 4),
            new Token(LParen, "(", 5, 1),
            new Token(StringLiteral, "$.store.book[0]", 6, 17),
            new Token(RParen, ")", 23, 1),
            new Token(Eof, string.Empty, 24, 0));
    }

    [Fact]
    public void Tokenize_FunctionWithMixedArguments_ProducesTokens()
    {
        Shape(Tokenize("@coalesce(@a.b, c.d, \"x\")")).Should().Equal(
            (At, "@"),
            (Identifier, "coalesce"),
            (LParen, "("),
            (At, "@"),
            (Identifier, "a"),
            (Dot, "."),
            (Identifier, "b"),
            (Comma, ","),
            (Identifier, "c"),
            (Dot, "."),
            (Identifier, "d"),
            (Comma, ","),
            (StringLiteral, "x"),
            (RParen, ")"),
            (Eof, string.Empty));
    }

    [Fact]
    public void Tokenize_StringLiteralWithEscapes_DecodesEscapedCharacters()
    {
        Shape(Tokenize("@f(\"a\\\"b\\\\c\")")).Should().Equal(
            (At, "@"),
            (Identifier, "f"),
            (LParen, "("),
            (StringLiteral, "a\"b\\c"),
            (RParen, ")"),
            (Eof, string.Empty));
    }

    [Fact]
    public void Tokenize_Interpolation_ProducesExactOffsets()
    {
        var tokens = Tokenize("@[Hello @name!]");

        tokens.Should().Equal(
            new Token(OpenInterpolation, "@[", 0, 2),
            new Token(Text, "Hello ", 2, 6),
            new Token(At, "@", 8, 1),
            new Token(Identifier, "name", 9, 4),
            new Token(Text, "!", 13, 1),
            new Token(CloseBracket, "]", 14, 1),
            new Token(Eof, string.Empty, 15, 0));
    }

    [Fact]
    public void Tokenize_InterpolationWithEscapes_DecodesTextVerbatim()
    {
        var tokens = Tokenize("@[a\\@b\\]c\\\\d]");

        tokens.Should().Equal(
            new Token(OpenInterpolation, "@[", 0, 2),
            new Token(Text, "a@b]c\\d", 2, 10),
            new Token(CloseBracket, "]", 12, 1),
            new Token(Eof, string.Empty, 13, 0));
    }

    [Fact]
    public void Tokenize_InterpolationWhitespace_IsPreserved()
    {
        Shape(Tokenize("@[  @x  ]")).Should().Equal(
            (OpenInterpolation, "@["),
            (Text, "  "),
            (At, "@"),
            (Identifier, "x"),
            (Text, "  "),
            (CloseBracket, "]"),
            (Eof, string.Empty));
    }

    [Fact]
    public void Tokenize_Math_ProducesArithmeticTokens()
    {
        Shape(Tokenize("@{1 + 2 * 3}")).Should().Equal(
            (OpenMath, "@{"),
            (Number, "1"),
            (Plus, "+"),
            (Number, "2"),
            (Star, "*"),
            (Number, "3"),
            (CloseBrace, "}"),
            (Eof, string.Empty));
    }

    [Fact]
    public void Tokenize_MathDecimals_ProducesNumberTokens()
    {
        Shape(Tokenize("@{3.14 + 0.5}")).Should().Equal(
            (OpenMath, "@{"),
            (Number, "3.14"),
            (Plus, "+"),
            (Number, "0.5"),
            (CloseBrace, "}"),
            (Eof, string.Empty));
    }

    [Fact]
    public void Tokenize_MathWithGroupingAndStatement_ProducesExactOffsets()
    {
        var tokens = Tokenize("@{(1 + @a.b) % 2}");

        tokens.Should().Equal(
            new Token(OpenMath, "@{", 0, 2),
            new Token(LParen, "(", 2, 1),
            new Token(Number, "1", 3, 1),
            new Token(Plus, "+", 5, 1),
            new Token(At, "@", 7, 1),
            new Token(Identifier, "a", 8, 1),
            new Token(Dot, ".", 9, 1),
            new Token(Identifier, "b", 10, 1),
            new Token(RParen, ")", 11, 1),
            new Token(Percent, "%", 13, 1),
            new Token(Number, "2", 15, 1),
            new Token(CloseBrace, "}", 16, 1),
            new Token(Eof, string.Empty, 17, 0));
    }

    [Fact]
    public void Tokenize_TrailingWhitespace_IsTolerated()
    {
        Shape(Tokenize("@path ")).Should().Equal((At, "@"), (Identifier, "path"), (Eof, string.Empty));
    }

    [Theory]
    [InlineData("@1", 1)]
    [InlineData("@", 1)]
    [InlineData("@a.", 3)]
    [InlineData("@f(\"abc)", 3)]
    [InlineData("@[abc", 0)]
    [InlineData("@{1+2", 0)]
    [InlineData("@[a\\b]", 3)]
    [InlineData("@f(a", 2)]
    [InlineData("@a.b c", 5)]
    [InlineData("@{3.}", 4)]
    [InlineData("abc", 0)]
    public void Tokenize_InvalidInput_ThrowsWithPosition(string source, int position)
    {
        var act = () => new Tokenizer(source).Tokenize();

        act.Should().Throw<BindingSyntaxException>().Which.Position.Should().Be(position);
    }

    private static IReadOnlyList<Token> Tokenize(string source)
    {
        return new Tokenizer(source).Tokenize();
    }

    private static (TokenType Type, string Lexeme)[] Shape(IReadOnlyList<Token> tokens)
    {
        return tokens.Select(token => (token.Type, token.Lexeme)).ToArray();
    }
}
