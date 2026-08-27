using System.Globalization;

namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// A recursive-descent parser that turns a token stream into a binding AST. Expressions never nest:
/// only statements may appear inside interpolations and math expressions, although statements may nest
/// because a function argument can itself be a statement.
/// </summary>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _index;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    private Token Current => _tokens[_index];

    public BindingNode Parse()
    {
        BindingNode node = Current.Type switch
        {
            TokenType.OpenInterpolation => ParseInterpolation(),
            TokenType.OpenMath => ParseMath(),
            TokenType.At => ParseStatement(),
            _ => throw Error("A binding must be a statement, interpolation, or math expression."),
        };

        Expect(TokenType.Eof, "Expected the end of the binding expression.");
        return node;
    }

    private bool Check(TokenType type)
    {
        return Current.Type == type;
    }

    private Token Advance()
    {
        return _tokens[_index++];
    }

    private bool Match(TokenType type)
    {
        if (!Check(type))
        {
            return false;
        }

        _index++;
        return true;
    }

    private Token Expect(TokenType type, string message)
    {
        if (!Check(type))
        {
            throw Error(message);
        }

        return Advance();
    }

    private BindingSyntaxException Error(string message)
    {
        return new BindingSyntaxException(message, Current.Start);
    }

    private Statement ParseStatement()
    {
        Expect(TokenType.At, "Expected '@' to start a statement.");

        if (Check(TokenType.LParen))
        {
            Advance();
            var path = ParsePath();
            Expect(TokenType.Comma, "Expected ',' between the path and source of a sourced binding.");
            var source = Expect(TokenType.Identifier, "Expected a source identifier in a sourced binding.");
            Expect(TokenType.RParen, "Expected ')' to close a sourced binding.");
            return new SourcedStatement(path, source.Lexeme);
        }

        if (Check(TokenType.Identifier))
        {
            var first = Advance();

            if (Check(TokenType.LParen))
            {
                Advance();
                var arguments = new List<BindingNode>();

                if (!Check(TokenType.RParen))
                {
                    do
                    {
                        arguments.Add(ParseArgument());
                    }
                    while (Match(TokenType.Comma));
                }

                Expect(TokenType.RParen, "Expected ')' to close a function call.");
                return new FunctionStatement(first.Lexeme, arguments);
            }

            var segments = new List<string> { first.Lexeme };
            while (Match(TokenType.Dot))
            {
                segments.Add(Expect(TokenType.Identifier, "Expected an identifier after '.'.").Lexeme);
            }

            return new PathStatement(new PathNode(segments));
        }

        throw Error("Expected a path, source, or function after '@'.");
    }

    private PathNode ParsePath()
    {
        var segments = new List<string>
        {
            Expect(TokenType.Identifier, "Expected an identifier in a path.").Lexeme,
        };

        while (Match(TokenType.Dot))
        {
            segments.Add(Expect(TokenType.Identifier, "Expected an identifier after '.'.").Lexeme);
        }

        return new PathNode(segments);
    }

    private BindingNode ParseArgument()
    {
        if (Check(TokenType.At))
        {
            return ParseStatement();
        }

        if (Check(TokenType.StringLiteral))
        {
            return new StringLiteralNode(Advance().Lexeme);
        }

        if (Check(TokenType.Identifier))
        {
            return ParsePath();
        }

        throw Error("Expected a function argument: a statement, path, or string literal.");
    }

    private InterpolationExpression ParseInterpolation()
    {
        Expect(TokenType.OpenInterpolation, "Expected '@[' to start an interpolation.");
        var parts = new List<BindingNode>();

        while (!Check(TokenType.CloseBracket))
        {
            if (Check(TokenType.Text))
            {
                parts.Add(new TextPart(Advance().Lexeme));
            }
            else if (Check(TokenType.At))
            {
                parts.Add(new StatementPart(ParseStatement()));
            }
            else
            {
                throw Error("Expected text or a statement inside an interpolation.");
            }
        }

        Expect(TokenType.CloseBracket, "Expected ']' to close an interpolation.");
        return new InterpolationExpression(parts);
    }

    private MathExpression ParseMath()
    {
        Expect(TokenType.OpenMath, "Expected '@{' to start a math expression.");
        var expression = ParseExpression();
        Expect(TokenType.CloseBrace, "Expected '}' to close a math expression.");
        return new MathExpression(expression);
    }

    private BindingNode ParseExpression()
    {
        var left = ParseTerm();

        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            var op = Advance();
            var right = ParseTerm();
            left = new BinaryNode(op.Lexeme[0], left, right);
        }

        return left;
    }

    private BindingNode ParseTerm()
    {
        var left = ParseFactor();

        while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Percent))
        {
            var op = Advance();
            var right = ParseFactor();
            left = new BinaryNode(op.Lexeme[0], left, right);
        }

        return left;
    }

    private BindingNode ParseFactor()
    {
        if (Check(TokenType.Number))
        {
            var number = Advance();
            return new NumberNode(decimal.Parse(number.Lexeme, CultureInfo.InvariantCulture));
        }

        if (Check(TokenType.At))
        {
            return new StatementOperand(ParseStatement());
        }

        if (Check(TokenType.LParen))
        {
            Advance();
            var expression = ParseExpression();
            Expect(TokenType.RParen, "Expected ')' in a math expression.");
            return expression;
        }

        throw Error("Expected a number, statement, or '(' in a math expression.");
    }
}
