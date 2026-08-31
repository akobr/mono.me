using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// The JSON entry point for data binding. It guards on the leading '@', tokenizes, parses, and
/// evaluates the expression, then assigns the result back onto the JSON token. Sources and functions
/// are registered through <see cref="IBindingRegistry"/>.
/// </summary>
public sealed class BindingExecutor : IBindingExecutor, IBindingRegistry
{
    public const string DefaultSourceKey = BindingEvaluator.DefaultSourceKey;

    private readonly ConcurrentDictionary<string, IBindingSource> _sources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IBindingFunction> _functions = new(StringComparer.Ordinal);

    public void RegisterSource(string key, IBindingSource source)
    {
        _sources[key] = source;
    }

    public void RegisterFunction(string name, IBindingFunction function)
    {
        _functions[name] = function;
    }

    public ValueTask<bool> TryBinding(JProperty property, bool includeSecrets, BindingScope? scope = null)
    {
        return BindAsync(property.Value, includeSecrets, scope, token => property.Value = token);
    }

    public ValueTask<bool> TryBinding(JValue value, bool includeSecrets, BindingScope? scope = null)
    {
        return BindAsync(value, includeSecrets, scope, token => value.Replace(token));
    }

    private async ValueTask<bool> BindAsync(JToken current, bool includeSecrets, BindingScope? scope, Action<JToken> apply)
    {
        if (current.Type != JTokenType.String)
        {
            return false;
        }

        var raw = current.Value<string>();
        if (string.IsNullOrEmpty(raw) || raw[0] != '@')
        {
            return false;
        }

        JToken? result;
        try
        {
            var tokens = new Tokenizer(raw).Tokenize();
            var ast = new Parser(tokens).Parse();
            var evaluator = new BindingEvaluator(_sources, _functions, includeSecrets, scope);
            result = await evaluator.EvaluateAsync(ast);
        }
        catch (BindingException exception)
        {
            throw new BindingException(
                $"Failed to process the data binding '{raw}' for '{current.Path}': {exception.Message}",
                exception);
        }

        if (result is null)
        {
            return false;
        }

        apply(result.DeepClone());
        return true;
    }
}
