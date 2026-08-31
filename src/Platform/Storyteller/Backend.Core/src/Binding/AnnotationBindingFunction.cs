using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Configuring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// Implements <c>@annotation("&lt;expr&gt;")</c> and <c>@annotation("&lt;expr&gt;", "&lt;annotationType&gt;")</c>,
/// resolving a JSONPath or JSON Pointer expression against the freeform <see cref="Annotation.Values"/> of an
/// annotation. With one argument, the target is the annotation whose key equals the configuration currently being
/// resolved (see <see cref="ConfigurationBindingContext"/>, supplied via <see cref="BindingScope.Context"/>). With a
/// second argument, the target is instead the ancestor annotation of the given <see cref="AnnotationType"/>.
/// </summary>
public sealed class AnnotationBindingFunction : IBindingFunction
{
    private const string FunctionName = "annotation";

    private readonly IAnnotationService _annotationService;

    public AnnotationBindingFunction(IAnnotationService annotationService)
    {
        _annotationService = annotationService ?? throw new ArgumentNullException(nameof(annotationService));
    }

    public async ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Arguments.Count is < 1 or > 2)
        {
            throw new BindingEvaluationException(
                $"'{FunctionName}' expects one or two arguments: a JSONPath or JSON Pointer expression and, optionally, an annotation type.");
        }

        if (request.Context is not ConfigurationBindingContext context)
        {
            throw new BindingEvaluationException(
                $"'{FunctionName}' can only be used while resolving a configuration.");
        }

        var expression = BindingFunctionArguments.RequireString(request.Arguments[0], FunctionName, 1);
        var targetKey = context.ConfigurationKey;

        if (request.Arguments.Count == 2)
        {
            var typeName = BindingFunctionArguments.RequireString(request.Arguments[1], FunctionName, 2);

            if (!Enum.TryParse<AnnotationType>(typeName, ignoreCase: true, out var annotationType))
            {
                throw new BindingEvaluationException(
                    $"'{typeName}' is not a known annotation type for function '{FunctionName}'.");
            }

            var ancestorKey = context.ConfigurationKey.Annotation.TryGetAncestorKey(annotationType)
                ?? throw new BindingEvaluationException(
                    $"'{context.ConfigurationKey.Annotation.Type}' has no '{annotationType}' ancestor.");

            targetKey = FullKey.Create(ancestorKey, context.ConfigurationKey);
        }

        var annotation = await _annotationService.GetAnnotationAsync(targetKey);
        if (annotation is null || annotation.Values is null)
        {
            return null;
        }

        var document = JObject.FromObject(annotation.Values);
        var result = JsonQuery.Resolve(document, expression);

        return result is null ? null : new BindingValue(result);
    }
}
