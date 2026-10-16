using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Api.Security;

// The scopes of the export and import of a view depend on the kinds they include (export-import.md, API): annotations
// need the Annotation scopes; schemas, templates and configurations the Configuration ones. A request needs the scopes
// of every kind it includes, and reading a job the scopes of every kind the job includes. A missing scope is 403, so
// a machine with only Configuration.ReadWrite learns to leave annotations out of include.
public static class ViewTransferScopes
{
    private static readonly string[] AnnotationRead = [Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write];
    private static readonly string[] AnnotationWrite = [Scopes.Annotation.Write, Scopes.Default.Write];
    private static readonly string[] ConfigurationRead = [Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write];
    private static readonly string[] ConfigurationWrite = [Scopes.Configuration.Write, Scopes.Default.Write];

    // Any one of them allows the kind: read scopes for an export and reading it, write scopes for an import and reading it.
    public static IReadOnlyList<string> GetScopes(ViewArchiveContent content, ViewTransferKind kind)
    {
        return (content, kind) switch
        {
            (ViewArchiveContent.Annotations, ViewTransferKind.Export) => AnnotationRead,
            (ViewArchiveContent.Annotations, _) => AnnotationWrite,
            (_, ViewTransferKind.Export) => ConfigurationRead,
            _ => ConfigurationWrite,
        };
    }

    // Throws AccessDeniedException when a kind of the include has none of its scopes among the granted ones. The include
    // is resolved first: null or empty means all four kinds. isJob words the message for reading a stored job.
    public static void Ensure(IReadOnlySet<string> granted, IReadOnlyList<ViewArchiveContent>? include, ViewTransferKind kind, bool isJob = false)
    {
        // the kinds that share a rule share its array, so they are grouped by reference
        var missing = ViewArchiveInclude.Resolve(include)
            .Where(content => !GetScopes(content, kind).Any(granted.Contains))
            .GroupBy(content => GetScopes(content, kind))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        var job = kind == ViewTransferKind.Export ? "export" : "import";
        var verb = kind == ViewTransferKind.Export ? "exporting" : "importing";
        var isAll = include is null || include.Count == 0;
        var where = isJob ? $" in the include of this {job}" : isAll ? string.Empty : " in include";
        var action = isJob ? "reading it" : $"{verb} them";
        var sentences = missing.Select(group =>
            $"Missing scope for {JoinAnd(group.Select(content => content.ToString().ToLowerInvariant()))}{where}: {action} needs {JoinOr(group.Key)}.");
        var message = string.Join(" ", sentences);

        if (!isJob && isAll)
        {
            message += $" Without include, all four kinds are included; name the kinds to {job} in include.";
        }

        throw new AccessDeniedException(message);
    }

    private static string JoinAnd(IEnumerable<string> values) => Join(values.ToList(), "and");

    private static string JoinOr(IEnumerable<string> values) => Join(values.ToList(), "or");

    private static string Join(IReadOnlyList<string> values, string conjunction)
    {
        return values.Count < 2
            ? string.Concat(values)
            : $"{string.Join(", ", values.Take(values.Count - 1))} {conjunction} {values[^1]}";
    }
}
