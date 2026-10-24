using System.Runtime.CompilerServices;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Reads what an @ expression refers to, following the grammar of Binding.Language (Tokenizer and Parser) without
// evaluating anything: paths, sourced paths, source and function calls, interpolations and math expressions.
//
// A call with exactly one bare path argument is a source call when the name is a registered source; this reader does not
// know the registrations, so it takes such a call as a source call (as @public, @secret and a Key Vault source are
// written). Bare path arguments of other calls are read through the default source, as the evaluator does.
//
// The reader recurses once per nested statement or parenthesis, and any stored string reaches it, so an expression
// longer than MaxLength or nested deeper than MaxDepth is not read: it counts as not valid, with ExceedsLimits.
public static class BindingExpressions
{
    public const int MaxLength = 8 * 1024;
    public const int MaxDepth = 64;

    public static readonly IReadOnlySet<string> ValuesStoreSources = new HashSet<string>(StringComparer.Ordinal) { "default", "public", "secret" };

    private static readonly ParsedBinding Invalid = new(BindingForm.Invalid, null, [], false, false);
    private static readonly ParsedBinding BeyondLimits = Invalid with { ExceedsLimits = true };

    // The resolver binds every string that starts with '@' (Binding.Object ConfigurationBindingResolver).
    public static bool IsBinding(string? value)
    {
        return !string.IsNullOrEmpty(value) && value[0] == '@';
    }

    public static ParsedBinding Parse(string expression)
    {
        if (expression.Length > MaxLength)
        {
            return BeyondLimits;
        }

        var reader = new Reader(expression);

        try
        {
            return reader.Read();
        }
        catch (FormatException)
        {
            return reader.ExceededLimits ? BeyondLimits : Invalid;
        }
    }

    private sealed class Reader(string text)
    {
        private readonly List<BindingReference> _references = [];
        private int _position;
        private int _depth;
        private bool _readsDocument;
        private bool _readsAnnotations;

        public bool ExceededLimits { get; private set; }

        private bool IsAtEnd => _position >= text.Length;

        private char Current => text[_position];

        public ParsedBinding Read()
        {
            BindingForm form;
            string? name = null;

            if (StartsWith("@["))
            {
                _position = 2;
                ReadInterpolation();
                form = BindingForm.Interpolation;
            }
            else if (StartsWith("@{"))
            {
                _position = 2;
                ReadExpression();
                Expect('}');
                form = BindingForm.Math;
            }
            else if (StartsWith("@"))
            {
                _position = 1;
                (form, name) = ReadStatement(inParentheses: false);
            }
            else
            {
                throw new FormatException();
            }

            SkipWhitespace();

            if (!IsAtEnd)
            {
                throw new FormatException();
            }

            return new ParsedBinding(form, name, _references.Distinct().ToList(), _readsDocument, _readsAnnotations);
        }

        private static bool IsIdentifierStart(char c)
        {
            return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';
        }

        private static bool IsIdentifierPart(char c)
        {
            return IsIdentifierStart(c) || char.IsAsciiDigit(c);
        }

        // After '@'. Inside parentheses the tokenizer skips whitespace between tokens; elsewhere a statement is compact.
        private (BindingForm Form, string? Name) ReadStatement(bool inParentheses)
        {
            Enter();

            try
            {
                SkipWhitespaceIf(inParentheses);

                if (IsAtEnd)
                {
                    throw new FormatException();
                }

                if (Current == '(')
                {
                    _position++;
                    SkipWhitespace();
                    var path = ReadPath(inParentheses: true);
                    Expect(',');
                    SkipWhitespace();
                    var source = ReadIdentifier();
                    Expect(')');
                    _references.Add(new BindingReference(path, source));
                    return (BindingForm.Sourced, source);
                }

                var name = ReadIdentifier();
                SkipWhitespaceIf(inParentheses);

                if (!IsAtEnd && Current == '(')
                {
                    _position++;
                    return ReadCall(name);
                }

                _references.Add(new BindingReference(name + ReadPathRest(inParentheses), "default"));
                return (BindingForm.Path, null);
            }
            finally
            {
                _depth--;
            }
        }

        // After '('.
        private (BindingForm Form, string? Name) ReadCall(string name)
        {
            var paths = new List<string?>();
            SkipWhitespace();

            if (!IsAtEnd && Current == ')')
            {
                _position++;
            }
            else
            {
                do
                {
                    SkipWhitespace();
                    paths.Add(ReadArgument());
                    SkipWhitespace();
                }
                while (TryConsume(','));

                Expect(')');
            }

            var bare = paths.OfType<string>().ToList();

            switch (name)
            {
                case "config":
                    _readsDocument = true;
                    AddDefault(bare);
                    return (BindingForm.Config, name);

                case "annotation":
                    _readsAnnotations = true;
                    AddDefault(bare);
                    return (BindingForm.Annotation, name);
            }

            if (paths.Count == 1 && bare.Count == 1)
            {
                _references.Add(new BindingReference(bare[0], name));
                return (BindingForm.SourceCall, name);
            }

            AddDefault(bare);
            return (BindingForm.Function, name);
        }

        // A path (returned), a string literal or a nested statement (null).
        private string? ReadArgument()
        {
            if (IsAtEnd)
            {
                throw new FormatException();
            }

            if (Current == '@')
            {
                _position++;
                ReadStatement(inParentheses: true);
                return null;
            }

            if (Current == '"')
            {
                ReadStringLiteral();
                return null;
            }

            return ReadPath(inParentheses: true);
        }

        private void AddDefault(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                _references.Add(new BindingReference(path, "default"));
            }
        }

        private void ReadInterpolation()
        {
            while (true)
            {
                if (IsAtEnd)
                {
                    throw new FormatException();
                }

                switch (Current)
                {
                    case '\\' when _position + 1 < text.Length && text[_position + 1] is '@' or ']' or '\\':
                        _position += 2;
                        break;

                    case '\\':
                        throw new FormatException();

                    case '@':
                        _position++;
                        ReadStatement(inParentheses: false);
                        break;

                    case ']':
                        _position++;
                        return;

                    default:
                        _position++;
                        break;
                }
            }
        }

        // The math grammar of Parser: expression := term (('+' | '-') term)*.
        private void ReadExpression()
        {
            ReadTerm();

            while (TryConsumeOperator("+-"))
            {
                ReadTerm();
            }
        }

        // term := factor (('*' | '/' | '%') factor)*
        private void ReadTerm()
        {
            ReadFactor();

            while (TryConsumeOperator("*/%"))
            {
                ReadFactor();
            }
        }

        // factor := number | '@' statement | '(' expression ')'. A sign is no factor, so @{-1} does not parse either.
        private void ReadFactor()
        {
            SkipWhitespace();

            if (IsAtEnd)
            {
                throw new FormatException();
            }

            if (char.IsAsciiDigit(Current))
            {
                ReadNumber();
            }
            else if (TryConsume('@'))
            {
                ReadStatement(inParentheses: false);
            }
            else if (TryConsume('('))
            {
                Enter();

                try
                {
                    ReadExpression();
                    Expect(')');
                }
                finally
                {
                    _depth--;
                }
            }
            else
            {
                throw new FormatException();
            }
        }

        // Digits with an optional fraction of at least one digit, as the tokenizer reads a number.
        private void ReadNumber()
        {
            SkipDigits();

            if (TryConsume('.'))
            {
                if (IsAtEnd || !char.IsAsciiDigit(Current))
                {
                    throw new FormatException();
                }

                SkipDigits();
            }
        }

        private void ReadStringLiteral()
        {
            _position++;

            while (true)
            {
                if (IsAtEnd)
                {
                    throw new FormatException();
                }

                if (Current == '\\')
                {
                    _position += 2;
                    continue;
                }

                _position++;

                if (text[_position - 1] == '"')
                {
                    return;
                }
            }
        }

        private string ReadPath(bool inParentheses)
        {
            return ReadIdentifier() + ReadPathRest(inParentheses);
        }

        private string ReadPathRest(bool inParentheses)
        {
            var rest = string.Empty;

            while (true)
            {
                var start = _position;
                SkipWhitespaceIf(inParentheses);

                if (IsAtEnd || Current != '.')
                {
                    _position = start;
                    return rest;
                }

                _position++;
                SkipWhitespaceIf(inParentheses);
                rest += "." + ReadIdentifier();
            }
        }

        private string ReadIdentifier()
        {
            if (IsAtEnd || !IsIdentifierStart(Current))
            {
                throw new FormatException();
            }

            var start = _position;

            while (!IsAtEnd && IsIdentifierPart(Current))
            {
                _position++;
            }

            return text[start.._position];
        }

        private bool StartsWith(string value)
        {
            return text.StartsWith(value, StringComparison.Ordinal);
        }

        private void Expect(char c)
        {
            SkipWhitespace();

            if (!TryConsume(c))
            {
                throw new FormatException();
            }
        }

        private bool TryConsume(char c)
        {
            if (IsAtEnd || Current != c)
            {
                return false;
            }

            _position++;
            return true;
        }

        private bool TryConsumeOperator(string operators)
        {
            SkipWhitespace();

            if (IsAtEnd || !operators.Contains(Current, StringComparison.Ordinal))
            {
                return false;
            }

            _position++;
            return true;
        }

        // A nested statement or parenthesis: beyond MaxDepth, or close to the end of the thread's stack, nothing is read.
        private void Enter()
        {
            _depth++;

            if (_depth > MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                ExceededLimits = true;
                throw new FormatException();
            }
        }

        private void SkipWhitespaceIf(bool condition)
        {
            if (condition)
            {
                SkipWhitespace();
            }
        }

        private void SkipWhitespace()
        {
            while (!IsAtEnd && char.IsWhiteSpace(Current))
            {
                _position++;
            }
        }

        private void SkipDigits()
        {
            while (!IsAtEnd && char.IsAsciiDigit(Current))
            {
                _position++;
            }
        }
    }
}
