using System.Text.Json;
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
/// The read records the storage marker of the target annotation (see <see cref="BindingScope.Dependencies"/>).
/// </summary>
public sealed class AnnotationBindingFunction : IBindingFunction, IDescribedBinding, IRecordingBinding
{
    private const string FunctionName = "annotation";

    private readonly Lazy<IAnnotationService> _annotationService;

    public AnnotationBindingFunction(Lazy<IAnnotationService> annotationService)
    {
        _annotationService = annotationService ?? throw new ArgumentNullException(nameof(annotationService));
    }

    public BindingDescription Describe(string name)
    {
        return new BindingDescription
        {
            Summary = "Reads a field of the `Values` of the annotation being configured, or of one of its ancestors.",
            Signature = $"{name}(\"<expr>\"[, \"<type>\"])",
            Parameters =
            [
                new BindingParameterDescription
                {
                    Name = "expr",
                    Description = "A quoted JSON Pointer (`/a/b`) or JSONPath (`$.a.b`) into the annotation's `Values`.",
                },
                new BindingParameterDescription
                {
                    Name = "type",
                    Description = "A quoted annotation type name. Reads the ancestor of that type instead of the annotation itself.",
                    IsOptional = true,
                    Values =
                    [
                        nameof(AnnotationType.Responsibility),
                        nameof(AnnotationType.Unit),
                        nameof(AnnotationType.Subject),
                        nameof(AnnotationType.Usage),
                        nameof(AnnotationType.Context),
                        nameof(AnnotationType.Execution),
                    ],
                },
            ],
            Examples =
            [
                new BindingExample
                {
                    Expression = $"@{name}(\"/owner\")",
                    Description = "The `owner` value of the annotation being configured.",
                },
                new BindingExample
                {
                    Expression = $"@{name}(\"/currency\", \"Subject\")",
                    Description = "The `currency` value of the subject the annotation belongs to.",
                },
            ],
        };
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

        var read = await _annotationService.Value.GetAnnotationWithMarkerAsync(targetKey);
        Record(request, targetKey, read.Marker);

        var annotation = read.Value;
        if (annotation is null || annotation.Values is null)
        {
            return null;
        }

        var document = JObject.Parse(JsonSerializer.Serialize(annotation.Values));
        var result = JsonQuery.Resolve(document, expression);

        return result is null ? null : new BindingValue(result);
    }

    private static void Record(BindingFunctionRequest request, FullKey targetKey, string? marker)
    {
        if (request.Dependencies is not { } dependencies)
        {
            return;
        }

        if (marker is null)
        {
            dependencies.MarkVolatile("The annotation service gives no storage marker.");
            return;
        }

        dependencies.Record(new BindingDependency(
            BindingDependencyKinds.Annotation,
            targetKey.ViewName,
            targetKey.Annotation.ToString(),
            marker));
    }
}
