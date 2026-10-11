namespace _42.Platform.Storyteller.Annotating;

// Rules for the content of an annotation, checked on every create, re-create and update.
// Stored annotations are not migrated: a legacy annotation breaking a rule stays readable until its next write.
public static class AnnotationRules
{
    public static void Validate(Annotation annotation)
    {
        var problems = GetProblems(annotation);

        if (problems.Count > 0)
        {
            throw new InvalidInputException(
                $"The annotation '{annotation.AnnotationKey}' is not valid: {string.Join(" ", problems)}",
                ErrorCodes.InvalidAnnotation);
        }
    }

    // One request with many annotations is rejected as a whole; the message lists every invalid key.
    public static void ValidateAll(IEnumerable<Annotation> annotations)
    {
        var invalid = annotations
            .Select(annotation => (annotation.AnnotationKey, Problems: GetProblems(annotation)))
            .Where(item => item.Problems.Count > 0)
            .ToList();

        if (invalid.Count > 0)
        {
            throw new InvalidInputException(
                $"{invalid.Count} annotation(s) are not valid: "
                + string.Join(" ", invalid.Select(item => $"'{item.AnnotationKey}': {string.Join(" ", item.Problems)}")),
                ErrorCodes.InvalidAnnotation);
        }
    }

    public static IReadOnlyList<string> GetProblems(Annotation annotation)
    {
        var problems = new List<string>();

        if (annotation.Title?.Length > AnnotationLimits.TitleMaxLength)
        {
            problems.Add($"Title has {annotation.Title.Length} characters, at most {AnnotationLimits.TitleMaxLength} are allowed.");
        }

        if (annotation.Description?.Length > AnnotationLimits.DescriptionMaxLength)
        {
            problems.Add($"Description has {annotation.Description.Length} characters, at most {AnnotationLimits.DescriptionMaxLength} are allowed.");
        }

        if (!string.IsNullOrEmpty(annotation.DocumentationLink))
        {
            if (annotation.DocumentationLink.Length > AnnotationLimits.DocumentationLinkMaxLength)
            {
                problems.Add($"DocumentationLink has {annotation.DocumentationLink.Length} characters, at most {AnnotationLimits.DocumentationLinkMaxLength} are allowed.");
            }
            else if (!IsHttpUrl(annotation.DocumentationLink))
            {
                problems.Add("DocumentationLink must be an absolute http or https URL.");
            }
        }

        return problems;
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !value.Any(char.IsWhiteSpace);
    }
}
