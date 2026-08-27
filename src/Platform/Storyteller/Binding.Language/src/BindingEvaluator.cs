using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// Evaluates a binding AST against a set of named sources and functions. A top-level statement may
/// resolve to <c>null</c> (the source/function declined or was not registered), in which case the
/// caller should leave the original value untouched. Interpolation and math always produce a value or
/// throw a <see cref="BindingEvaluationException"/> when an inner statement cannot be resolved.
/// </summary>
public sealed class BindingEvaluator
{
    public const string DefaultSourceKey = "default";

    private readonly IReadOnlyDictionary<string, IBindingSource> _sources;
    private readonly IReadOnlyDictionary<string, IBindingFunction> _functions;
    private readonly bool _includeSecrets;

    public BindingEvaluator(
        IReadOnlyDictionary<string, IBindingSource> sources,
        IReadOnlyDictionary<string, IBindingFunction> functions,
        bool includeSecrets)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _functions = functions ?? throw new ArgumentNullException(nameof(functions));
        _includeSecrets = includeSecrets;
    }

    public ValueTask<JToken?> EvaluateAsync(BindingNode node)
    {
        return node switch
        {
            Statement statement => EvaluateStatementAsync(statement),
            InterpolationExpression interpolation => EvaluateInterpolationAsync(interpolation),
            MathExpression math => EvaluateMathAsync(math),
            _ => throw new BindingEvaluationException(
                $"Cannot evaluate a binding node of type '{node.GetType().Name}'."),
        };
    }

    private static decimal Apply(char @operator, decimal left, decimal right)
    {
        switch (@operator)
        {
            case '+':
                return left + right;

            case '-':
                return left - right;

            case '*':
                return left * right;

            case '/':
                if (right == 0m)
                {
                    throw new BindingEvaluationException("Division by zero in a math expression.");
                }

                return left / right;

            case '%':
                if (right == 0m)
                {
                    throw new BindingEvaluationException("Modulo by zero in a math expression.");
                }

                return left % right;

            default:
                throw new BindingEvaluationException($"Unsupported math operator '{@operator}'.");
        }
    }

    private static decimal ToDecimal(JToken token)
    {
        if (token is JValue { Type: JTokenType.Integer or JTokenType.Float, Value: { } raw })
        {
            return Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
        }

        throw new BindingEvaluationException(
            $"Operand '{token.ToString(Formatting.None)}' is not a number.");
    }

    private static string Stringify(JToken token)
    {
        return token switch
        {
            JValue { Type: JTokenType.String } value => value.Value?.ToString() ?? string.Empty,
            JValue { Type: JTokenType.Null } => "null",
            _ => token.ToString(Formatting.None),
        };
    }

    private async ValueTask<JToken?> EvaluateStatementAsync(Statement statement)
    {
        switch (statement)
        {
            case PathStatement path:
                return await ResolveSourceAsync(DefaultSourceKey, path.Path.Segments);

            case SourcedStatement sourced:
                return await ResolveSourceAsync(sourced.Source, sourced.Path.Segments);

            case FunctionStatement function:
                return await InvokeFunctionAsync(function);

            default:
                throw new BindingEvaluationException(
                    $"Cannot evaluate a statement of type '{statement.GetType().Name}'.");
        }
    }

    private async ValueTask<JToken?> ResolveSourceAsync(string key, IReadOnlyList<string> path)
    {
        if (!_sources.TryGetValue(key, out var source))
        {
            return null;
        }

        var request = new BindingRequest { Path = path, IncludeSecrets = _includeSecrets };
        var value = await source.ResolveAsync(request);
        return value?.Token;
    }

    private async ValueTask<JToken?> InvokeFunctionAsync(FunctionStatement function)
    {
        if (!_functions.TryGetValue(function.Name, out var binding))
        {
            return null;
        }

        var arguments = new List<BindingValue>(function.Arguments.Count);
        foreach (var argument in function.Arguments)
        {
            var value = await EvaluateArgumentAsync(argument);
            if (value is null)
            {
                throw new BindingEvaluationException(
                    $"An argument of function '{function.Name}' could not be resolved.");
            }

            arguments.Add(value);
        }

        var request = new BindingFunctionRequest
        {
            Name = function.Name,
            Arguments = arguments,
            IncludeSecrets = _includeSecrets,
        };

        var result = await binding.InvokeAsync(request);
        return result?.Token;
    }

    private async ValueTask<BindingValue?> EvaluateArgumentAsync(BindingNode argument)
    {
        switch (argument)
        {
            case StringLiteralNode literal:
                return new BindingValue(new JValue(literal.Value));

            case PathNode path:
                var resolved = await ResolveSourceAsync(DefaultSourceKey, path.Segments);
                return resolved is null ? null : new BindingValue(resolved);

            case Statement statement:
                var value = await EvaluateStatementAsync(statement);
                return value is null ? null : new BindingValue(value);

            default:
                throw new BindingEvaluationException(
                    $"Cannot evaluate an argument of type '{argument.GetType().Name}'.");
        }
    }

    private async ValueTask<JToken?> EvaluateInterpolationAsync(InterpolationExpression interpolation)
    {
        var builder = new StringBuilder();

        foreach (var part in interpolation.Parts)
        {
            switch (part)
            {
                case TextPart text:
                    builder.Append(text.Text);
                    break;

                case StatementPart statementPart:
                    var value = await EvaluateStatementAsync(statementPart.Statement);
                    if (value is null)
                    {
                        throw new BindingEvaluationException("An interpolated statement could not be resolved.");
                    }

                    builder.Append(Stringify(value));
                    break;

                default:
                    throw new BindingEvaluationException(
                        $"Cannot evaluate an interpolation part of type '{part.GetType().Name}'.");
            }
        }

        return new JValue(builder.ToString());
    }

    private async ValueTask<JToken?> EvaluateMathAsync(MathExpression math)
    {
        var value = await EvaluateNumericAsync(math.Expression);
        return new JValue(value);
    }

    private async ValueTask<decimal> EvaluateNumericAsync(BindingNode node)
    {
        switch (node)
        {
            case NumberNode number:
                return number.Value;

            case StatementOperand operand:
                var value = await EvaluateStatementAsync(operand.Statement);
                if (value is null)
                {
                    throw new BindingEvaluationException("A math operand could not be resolved.");
                }

                return ToDecimal(value);

            case BinaryNode binary:
                var left = await EvaluateNumericAsync(binary.Left);
                var right = await EvaluateNumericAsync(binary.Right);
                return Apply(binary.Operator, left, right);

            default:
                throw new BindingEvaluationException(
                    $"Cannot evaluate a math operand of type '{node.GetType().Name}'.");
        }
    }
}
