using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Api.Security;

// The machine scopes of the export and import of a view depend on the kinds they include (export-import.md, API):
// annotations need Annotation.Read to export and Annotation.Write to import; schemas, templates and configurations
// Configuration.Read and Configuration.Write. A machine needs the scope of every kind a request includes, and reading a
// job the scope of every kind the job includes, granted to it (stored on the machine and carried by its credentials).
// A missing scope is 403, so a machine with only Configuration.Write learns to leave annotations out of include.
// Users need no data scopes: the Administrator role decides.
public static class ViewTransferScopes
{
    // One scope per kind and direction: the read scope for an export and reading it, the write scope for an import
    // and reading it.
    public static MachineScope GetScope(ViewArchiveContent content, ViewTransferKind kind)
    {
        return (content, kind) switch
        {
            (ViewArchiveContent.Annotations, ViewTransferKind.Export) => MachineScope.AnnotationRead,
            (ViewArchiveContent.Annotations, _) => MachineScope.AnnotationWrite,
            (_, ViewTransferKind.Export) => MachineScope.ConfigurationRead,
            _ => MachineScope.ConfigurationWrite,
        };
    }

    // Throws AccessDeniedException when the scope of a kind of the include is not among the granted ones. The include
    // is resolved first: null or empty means all four kinds. isJob words the message for reading a stored job.
    public static void Ensure(IReadOnlySet<MachineScope> granted, IReadOnlyList<ViewArchiveContent>? include, ViewTransferKind kind, bool isJob = false)
    {
        // the kinds that share a scope share its sentence
        var missing = ViewArchiveInclude.Resolve(include)
            .Where(content => !granted.Contains(GetScope(content, kind)))
            .GroupBy(content => GetScope(content, kind))
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
            $"Missing scope for {JoinAnd(group.Select(content => content.ToString().ToLowerInvariant()))}{where}: {action} needs {MachineScopes.ToScopeString(group.Key)}.");
        var message = string.Join(" ", sentences);

        if (!isJob && isAll)
        {
            message += $" Without include, all four kinds are included; name the kinds to {job} in include.";
        }

        throw new AccessDeniedException(message);
    }

    private static string JoinAnd(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count < 2
            ? string.Concat(list)
            : $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}";
    }
}
