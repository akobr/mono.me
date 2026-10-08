using System.Text;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Rewrites JSON-e <c>+</c> and <c>${...}</c> so concatenation goes through a length-checked function.
/// An expression that contains <c>+</c> and cannot be rewritten is rejected.
/// </summary>
internal static class JsonEExpressionRewriter
{
    public const string AddFunction = "storytellerAdd";

    public const string ConcatFunction = "storytellerConcat";

    public const string BoundFunction = "storytellerBound";

    public const string StepFunction = "storytellerStep";

    public static string RewritePlus(string expression)
    {
        if (!ContainsPlus(expression))
        {
            return expression;
        }

        return new Parser(expression).Rewrite();
    }

    public static bool TryRewriteInterpolation(string text, out string expression)
    {
        expression = string.Empty;
        if (!ContainsInterpolation(text))
        {
            return false;
        }

        var parts = new List<string>();
        var literal = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            if (IsInterpolation(text, index))
            {
                if (IsEscapedInterpolation(text, index))
                {
                    // The extra '$' is already in the literal. Keep `${...}` as text.
                    literal.Append('{');
                    index += 2;
                    var depth = 1;
                    while (index < text.Length && depth > 0)
                    {
                        var current = text[index];
                        if (current == '{')
                        {
                            depth++;
                        }
                        else if (current == '}')
                        {
                            depth--;
                        }

                        literal.Append(current);
                        index++;
                    }

                    continue;
                }

                FlushLiteral(literal, parts);
                index += 2;
                var start = index;
                if (!TrySkipExpression(text, ref index))
                {
                    throw new BindingEvaluationException("JSON-e expression could not be bounded.");
                }

                parts.Add(RewritePlus(text[start..index]));
                index++;
                continue;
            }

            literal.Append(text[index]);
            index++;
        }

        FlushLiteral(literal, parts);
        expression = $"{ConcatFunction}({string.Join(",", parts)})";
        return true;
    }

    private static void FlushLiteral(StringBuilder literal, List<string> parts)
    {
        if (literal.Length == 0)
        {
            return;
        }

        parts.Add(Quote(literal.ToString()));
        literal.Clear();
    }

    private static string Quote(string text)
    {
        return "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
    }

    private static bool ContainsPlus(string expression)
    {
        char? quote = null;
        for (var index = 0; index < expression.Length; index++)
        {
            var current = expression[index];
            if (quote is not null)
            {
                if (current == '\\')
                {
                    index++;
                    continue;
                }

                if (current == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }

            if (current == '+')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInterpolation(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (IsInterpolation(text, index) && !IsEscapedInterpolation(text, index))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInterpolation(string text, int index)
    {
        return index + 1 < text.Length && text[index] == '$' && text[index + 1] == '{';
    }

    private static bool IsEscapedInterpolation(string text, int index)
    {
        return index > 0 && text[index - 1] == '$' && IsInterpolation(text, index);
    }

    private static bool TrySkipExpression(string text, ref int index)
    {
        var depth = 1;
        char? quote = null;
        while (index < text.Length && depth > 0)
        {
            var current = text[index];
            if (quote is not null)
            {
                if (current == '\\')
                {
                    index += 2;
                    continue;
                }

                if (current == quote)
                {
                    quote = null;
                }

                index++;
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                index++;
                continue;
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return true;
                }
            }

            index++;
        }

        return false;
    }

    private sealed class Parser
    {
        private readonly string _source;
        private int _index;

        public Parser(string source)
        {
            _source = source;
        }

        public string Rewrite()
        {
            var rewritten = ParseExpression(0);
            SkipWhitespace();
            if (_index != _source.Length)
            {
                throw new BindingEvaluationException("JSON-e expression could not be bounded.");
            }

            return rewritten;
        }

        private string ParseExpression(int minPrecedence)
        {
            var left = ParseUnary();
            while (true)
            {
                var saved = _index;
                if (!TryReadBinary(out var op, out var precedence) || precedence < minPrecedence)
                {
                    _index = saved;
                    break;
                }

                var nextMin = op == "**" ? precedence : precedence + 1;
                var right = ParseExpression(nextMin);
                left = op == "+"
                    ? $"{AddFunction}({left},{right})"
                    : left + op + right;
            }

            return left;
        }

        private string ParseUnary()
        {
            SkipWhitespace();
            if (_index < _source.Length && _source[_index] is '+' or '-' or '!')
            {
                var op = _source[_index];
                _index++;
                return op + ParseUnary();
            }

            return ParseAtom();
        }

        private string ParseAtom()
        {
            SkipWhitespace();
            if (_index >= _source.Length)
            {
                throw new BindingEvaluationException("JSON-e expression could not be bounded.");
            }

            var current = _source[_index];
            string atom;
            if (current == '(')
            {
                _index++;
                var inner = ParseExpression(0);
                SkipWhitespace();
                Expect(')');
                atom = "(" + inner + ")";
            }
            else if (current == '[')
            {
                atom = ParseList('[', ']');
            }
            else if (current is '\'' or '"')
            {
                atom = ParseString();
            }
            else if (char.IsDigit(current) || (current == '.' && _index + 1 < _source.Length && char.IsDigit(_source[_index + 1])))
            {
                atom = ParseNumber();
            }
            else if (IsIdentStart(current))
            {
                var start = _index;
                _index++;
                while (_index < _source.Length && IsIdentChar(_source[_index]))
                {
                    _index++;
                }

                atom = _source[start.._index];
            }
            else
            {
                throw new BindingEvaluationException("JSON-e expression could not be bounded.");
            }

            return AppendSuffix(atom);
        }

        private string AppendSuffix(string atom)
        {
            while (true)
            {
                var saved = _index;
                SkipWhitespace();
                if (_index >= _source.Length)
                {
                    _index = saved;
                    return atom;
                }

                if (_source[_index] == '(')
                {
                    atom = atom + ParseList('(', ')');
                    continue;
                }

                if (_source[_index] == '[')
                {
                    _index++;
                    var index = ParseExpression(0);
                    SkipWhitespace();
                    Expect(']');
                    atom = atom + "[" + index + "]";
                    continue;
                }

                if (_source[_index] == '.')
                {
                    _index++;
                    SkipWhitespace();
                    if (_index >= _source.Length || !IsIdentStart(_source[_index]))
                    {
                        throw new BindingEvaluationException("JSON-e expression could not be bounded.");
                    }

                    var start = _index;
                    _index++;
                    while (_index < _source.Length && IsIdentChar(_source[_index]))
                    {
                        _index++;
                    }

                    atom = atom + "." + _source[start.._index];
                    continue;
                }

                _index = saved;
                return atom;
            }
        }

        private string ParseList(char open, char close)
        {
            Expect(open);
            SkipWhitespace();
            if (_index < _source.Length && _source[_index] == close)
            {
                _index++;
                return open + close.ToString();
            }

            var parts = new List<string>();
            while (true)
            {
                parts.Add(ParseExpression(0));
                SkipWhitespace();
                if (_index >= _source.Length)
                {
                    throw new BindingEvaluationException("JSON-e expression could not be bounded.");
                }

                if (_source[_index] == ',')
                {
                    _index++;
                    continue;
                }

                if (_source[_index] == close)
                {
                    _index++;
                    break;
                }

                throw new BindingEvaluationException("JSON-e expression could not be bounded.");
            }

            return open + string.Join(",", parts) + close;
        }

        private string ParseString()
        {
            var quote = _source[_index];
            var start = _index;
            _index++;
            while (_index < _source.Length)
            {
                var current = _source[_index];
                if (current == '\\')
                {
                    _index += 2;
                    continue;
                }

                _index++;
                if (current == quote)
                {
                    return _source[start.._index];
                }
            }

            throw new BindingEvaluationException("JSON-e expression could not be bounded.");
        }

        private string ParseNumber()
        {
            var start = _index;
            while (_index < _source.Length && char.IsDigit(_source[_index]))
            {
                _index++;
            }

            if (_index < _source.Length && _source[_index] == '.')
            {
                _index++;
                while (_index < _source.Length && char.IsDigit(_source[_index]))
                {
                    _index++;
                }
            }

            if (_index < _source.Length && _source[_index] is 'e' or 'E')
            {
                _index++;
                if (_index < _source.Length && _source[_index] is '+' or '-')
                {
                    _index++;
                }

                while (_index < _source.Length && char.IsDigit(_source[_index]))
                {
                    _index++;
                }
            }

            return _source[start.._index];
        }

        private bool TryReadBinary(out string text, out int precedence)
        {
            text = string.Empty;
            precedence = 0;
            var saved = _index;
            SkipWhitespace();
            if (_index >= _source.Length)
            {
                _index = saved;
                return false;
            }

            var rest = _source.AsSpan(_index);
            if (TryTake("**", 7, out text, out precedence) ||
                TryTake("&&", 1, out text, out precedence) ||
                TryTake("||", 2, out text, out precedence) ||
                TryTake("==", 3, out text, out precedence) ||
                TryTake("!=", 3, out text, out precedence) ||
                TryTake("<=", 3, out text, out precedence) ||
                TryTake(">=", 3, out text, out precedence))
            {
                return true;
            }

            if (rest.Length >= 2 &&
                rest[0] == 'i' &&
                rest[1] == 'n' &&
                (rest.Length == 2 || !IsIdentChar(rest[2])))
            {
                text = " in ";
                precedence = 4;
                _index += 2;
                return true;
            }

            switch (rest[0])
            {
                case '*':
                    text = "*";
                    precedence = 6;
                    break;
                case '/':
                    text = "/";
                    precedence = 6;
                    break;
                case '+':
                    text = "+";
                    precedence = 5;
                    break;
                case '-':
                    text = "-";
                    precedence = 5;
                    break;
                case '<':
                    text = "<";
                    precedence = 3;
                    break;
                case '>':
                    text = ">";
                    precedence = 3;
                    break;
                default:
                    _index = saved;
                    return false;
            }

            _index++;
            return true;
        }

        private bool TryTake(string token, int tokenPrecedence, out string text, out int precedence)
        {
            if (_source.AsSpan(_index).StartsWith(token))
            {
                text = token;
                precedence = tokenPrecedence;
                _index += token.Length;
                return true;
            }

            text = string.Empty;
            precedence = 0;
            return false;
        }

        private void Expect(char expected)
        {
            SkipWhitespace();
            if (_index >= _source.Length || _source[_index] != expected)
            {
                throw new BindingEvaluationException("JSON-e expression could not be bounded.");
            }

            _index++;
        }

        private void SkipWhitespace()
        {
            while (_index < _source.Length && char.IsWhiteSpace(_source[_index]))
            {
                _index++;
            }
        }

        private static bool IsIdentStart(char value)
        {
            return value is '_' || char.IsLetter(value);
        }

        private static bool IsIdentChar(char value)
        {
            return value is '_' || char.IsLetterOrDigit(value);
        }
    }
}
