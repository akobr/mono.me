using System.Text;

namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// Turns a binding expression into a flat list of <see cref="Token"/>s. The whole value is a single
/// binding that begins with '@'. The tokenizer recognizes three top-level forms: a statement ('@...'),
/// an interpolation ('@[...]'), and a math expression ('@{...}'). '@[' and '@{' are only special at the
/// top level; once inside an interpolation or math expression an '@' always begins a nested statement.
/// </summary>
public sealed class Tokenizer
{
    private readonly string _source;
    private int _position;

    public Tokenizer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    private bool IsAtEnd => _position >= _source.Length;

    private char Current => _source[_position];

    public IReadOnlyList<Token> Tokenize()
    {
        var tokens = new List<Token>();

        if (_source.Length == 0 || _source[0] != '@')
        {
            throw new BindingSyntaxException("A binding expression must start with '@'.", 0);
        }

        if (StartsWith("@["))
        {
            tokens.Add(new Token(TokenType.OpenInterpolation, "@[", 0, 2));
            _position = 2;
            LexInterpolation(tokens);
        }
        else if (StartsWith("@{"))
        {
            tokens.Add(new Token(TokenType.OpenMath, "@{", 0, 2));
            _position = 2;
            LexMath(tokens);
        }
        else
        {
            tokens.Add(new Token(TokenType.At, "@", 0, 1));
            _position = 1;
            LexStatement(tokens);
        }

        SkipWhitespace();
        if (!IsAtEnd)
        {
            throw new BindingSyntaxException(
                $"Unexpected character '{Current}' after the binding expression.",
                _position);
        }

        tokens.Add(new Token(TokenType.Eof, string.Empty, _position, 0));
        return tokens;
    }

    private static bool IsIdentifierStart(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    private static bool IsIdentifierPart(char c)
    {
        return IsIdentifierStart(c) || (c >= '0' && c <= '9');
    }

    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    private bool StartsWith(string value)
    {
        return _position + value.Length <= _source.Length
            && string.CompareOrdinal(_source, _position, value, 0, value.Length) == 0;
    }

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _source.Length ? _source[index] : '\0';
    }

    private void SkipWhitespace()
    {
        while (!IsAtEnd && char.IsWhiteSpace(Current))
        {
            _position++;
        }
    }

    private void LexStatement(List<Token> tokens)
    {
        if (IsAtEnd)
        {
            throw new BindingSyntaxException("Expected a path, source, or function after '@'.", _position);
        }

        if (Current == '(')
        {
            tokens.Add(new Token(TokenType.LParen, "(", _position, 1));
            _position++;
            LexBalancedParentheses(tokens);
            return;
        }

        if (IsIdentifierStart(Current))
        {
            ReadIdentifier(tokens);

            if (!IsAtEnd && Current == '(')
            {
                tokens.Add(new Token(TokenType.LParen, "(", _position, 1));
                _position++;
                LexBalancedParentheses(tokens);
                return;
            }

            while (!IsAtEnd && Current == '.')
            {
                tokens.Add(new Token(TokenType.Dot, ".", _position, 1));
                _position++;

                if (IsAtEnd || !IsIdentifierStart(Current))
                {
                    throw new BindingSyntaxException("Expected an identifier after '.'.", _position);
                }

                ReadIdentifier(tokens);
            }

            return;
        }

        throw new BindingSyntaxException(
            $"Expected a path, source, or function after '@' but found '{Current}'.",
            _position);
    }

    private void LexBalancedParentheses(List<Token> tokens)
    {
        var openPosition = _position - 1;
        var depth = 1;

        while (depth > 0)
        {
            SkipWhitespace();

            if (IsAtEnd)
            {
                throw new BindingSyntaxException("Unterminated '(' in binding statement.", openPosition);
            }

            var c = Current;
            switch (c)
            {
                case '(':
                    tokens.Add(new Token(TokenType.LParen, "(", _position, 1));
                    _position++;
                    depth++;
                    break;

                case ')':
                    tokens.Add(new Token(TokenType.RParen, ")", _position, 1));
                    _position++;
                    depth--;
                    break;

                case ',':
                    tokens.Add(new Token(TokenType.Comma, ",", _position, 1));
                    _position++;
                    break;

                case '.':
                    tokens.Add(new Token(TokenType.Dot, ".", _position, 1));
                    _position++;
                    break;

                case '@':
                    tokens.Add(new Token(TokenType.At, "@", _position, 1));
                    _position++;
                    break;

                case '"':
                    ReadStringLiteral(tokens);
                    break;

                default:
                    if (IsIdentifierStart(c))
                    {
                        ReadIdentifier(tokens);
                    }
                    else
                    {
                        throw new BindingSyntaxException(
                            $"Unexpected character '{c}' in binding statement.",
                            _position);
                    }

                    break;
            }
        }
    }

    private void LexInterpolation(List<Token> tokens)
    {
        var builder = new StringBuilder();
        var textStart = _position;

        while (true)
        {
            if (IsAtEnd)
            {
                throw new BindingSyntaxException("Unterminated interpolation '@[' (missing ']').", 0);
            }

            var c = Current;

            if (c == '\\')
            {
                var next = Peek(1);
                if (next is '@' or ']' or '\\')
                {
                    builder.Append(next);
                    _position += 2;
                    continue;
                }

                throw new BindingSyntaxException(
                    "Invalid escape sequence in interpolation; only '\\@', '\\]', and '\\\\' are supported.",
                    _position);
            }

            if (c == '@')
            {
                FlushText(tokens, builder, textStart);
                tokens.Add(new Token(TokenType.At, "@", _position, 1));
                _position++;
                LexStatement(tokens);
                textStart = _position;
                continue;
            }

            if (c == ']')
            {
                FlushText(tokens, builder, textStart);
                tokens.Add(new Token(TokenType.CloseBracket, "]", _position, 1));
                _position++;
                return;
            }

            builder.Append(c);
            _position++;
        }
    }

    private void LexMath(List<Token> tokens)
    {
        while (true)
        {
            SkipWhitespace();

            if (IsAtEnd)
            {
                throw new BindingSyntaxException("Unterminated math expression '@{' (missing '}').", 0);
            }

            var c = Current;
            switch (c)
            {
                case '}':
                    tokens.Add(new Token(TokenType.CloseBrace, "}", _position, 1));
                    _position++;
                    return;

                case '@':
                    tokens.Add(new Token(TokenType.At, "@", _position, 1));
                    _position++;
                    LexStatement(tokens);
                    break;

                case '(':
                    tokens.Add(new Token(TokenType.LParen, "(", _position, 1));
                    _position++;
                    break;

                case ')':
                    tokens.Add(new Token(TokenType.RParen, ")", _position, 1));
                    _position++;
                    break;

                case '+':
                    tokens.Add(new Token(TokenType.Plus, "+", _position, 1));
                    _position++;
                    break;

                case '-':
                    tokens.Add(new Token(TokenType.Minus, "-", _position, 1));
                    _position++;
                    break;

                case '*':
                    tokens.Add(new Token(TokenType.Star, "*", _position, 1));
                    _position++;
                    break;

                case '/':
                    tokens.Add(new Token(TokenType.Slash, "/", _position, 1));
                    _position++;
                    break;

                case '%':
                    tokens.Add(new Token(TokenType.Percent, "%", _position, 1));
                    _position++;
                    break;

                default:
                    if (IsDigit(c))
                    {
                        ReadNumber(tokens);
                    }
                    else
                    {
                        throw new BindingSyntaxException(
                            $"Unexpected character '{c}' in math expression.",
                            _position);
                    }

                    break;
            }
        }
    }

    private void ReadIdentifier(List<Token> tokens)
    {
        var start = _position;
        while (!IsAtEnd && IsIdentifierPart(Current))
        {
            _position++;
        }

        var lexeme = _source.Substring(start, _position - start);
        tokens.Add(new Token(TokenType.Identifier, lexeme, start, lexeme.Length));
    }

    private void ReadNumber(List<Token> tokens)
    {
        var start = _position;
        while (!IsAtEnd && IsDigit(Current))
        {
            _position++;
        }

        if (!IsAtEnd && Current == '.')
        {
            _position++;
            if (IsAtEnd || !IsDigit(Current))
            {
                throw new BindingSyntaxException("Expected a digit after the decimal point.", _position);
            }

            while (!IsAtEnd && IsDigit(Current))
            {
                _position++;
            }
        }

        var lexeme = _source.Substring(start, _position - start);
        tokens.Add(new Token(TokenType.Number, lexeme, start, lexeme.Length));
    }

    private void ReadStringLiteral(List<Token> tokens)
    {
        var start = _position;
        _position++;
        var builder = new StringBuilder();

        while (true)
        {
            if (IsAtEnd)
            {
                throw new BindingSyntaxException("Unterminated string literal.", start);
            }

            var c = Current;

            if (c == '\\')
            {
                _position++;
                if (IsAtEnd)
                {
                    throw new BindingSyntaxException("Unterminated string literal.", start);
                }

                builder.Append(Current);
                _position++;
                continue;
            }

            if (c == '"')
            {
                _position++;
                break;
            }

            builder.Append(c);
            _position++;
        }

        tokens.Add(new Token(TokenType.StringLiteral, builder.ToString(), start, _position - start));
    }

    private void FlushText(List<Token> tokens, StringBuilder builder, int textStart)
    {
        if (builder.Length == 0)
        {
            return;
        }

        tokens.Add(new Token(TokenType.Text, builder.ToString(), textStart, _position - textStart));
        builder.Clear();
    }
}
