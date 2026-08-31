namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// Implements <c>@config("&lt;expr&gt;")</c>, resolving a JSONPath or JSON Pointer expression against the
/// configuration document currently being resolved (see <see cref="BindingScope.Document"/>). The expression is
/// always evaluated against a snapshot of the configuration taken before the binding pass started, so the result
/// never depends on the order in which properties are processed.
/// </summary>
public sealed class ConfigBindingFunction : IBindingFunction
{
    private const string FunctionName = "config";

    public ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Arguments.Count != 1)
        {
            throw new BindingEvaluationException(
                $"'{FunctionName}' expects exactly one argument: a JSONPath or JSON Pointer expression.");
        }

        if (request.Document is null)
        {
            throw new BindingEvaluationException(
                $"'{FunctionName}' requires the current configuration document, but none was supplied.");
        }

        var expression = BindingFunctionArguments.RequireString(request.Arguments[0], FunctionName, 1);
        var result = JsonQuery.Resolve(request.Document, expression);

        return new ValueTask<BindingValue?>(result is null ? null : new BindingValue(result));
    }
}
