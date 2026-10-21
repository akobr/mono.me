using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Api.Security;

// The machine scopes of the export and import of a view depend on the kinds they include (export-import.md, API):
// annotations need Annotation.Read to export, schemas, templates and configurations Configuration.Read. An import writes
// what the direct routes write, so it needs their scopes: Annotation.Write and Annotation.Manage for annotations (it
// creates them in bulk and re-creates deleted ones), Configuration.Write and Schema.Write for schemas,
// Configuration.Write and Template.Write for templates, and Configuration.Write for configurations; a view import is no
// way around a management scope. A machine needs every scope of every kind a request includes, and reading a job those
// of every kind the job includes, granted to it (stored on the machine and carried by its credentials). A missing scope
// is 403 that names the scopes of the kind, so a machine learns what to leave out of include. Users need no data
// scopes: the Administrator role decides.
public static class ViewTransferScopes
{
    private static readonly IReadOnlyList<MachineScope> AnnotationExport = [MachineScope.AnnotationRead];
    private static readonly IReadOnlyList<MachineScope> ConfigurationExport = [MachineScope.ConfigurationRead];
    private static readonly IReadOnlyList<MachineScope> AnnotationImport = [MachineScope.AnnotationWrite, MachineScope.AnnotationManage];
    private static readonly IReadOnlyList<MachineScope> SchemaImport = [MachineScope.ConfigurationWrite, MachineScope.SchemaWrite];
    private static readonly IReadOnlyList<MachineScope> TemplateImport = [MachineScope.ConfigurationWrite, MachineScope.TemplateWrite];
    private static readonly IReadOnlyList<MachineScope> ConfigurationImport = [MachineScope.ConfigurationWrite];

    // The scopes of a kind and direction, all required: the read scope for an export and reading it, the scopes of the
    // direct writes for an import and reading it.
    public static IReadOnlyList<MachineScope> GetScopes(ViewArchiveContent content, ViewTransferKind kind)
    {
        return (content, kind) switch
        {
            (ViewArchiveContent.Annotations, ViewTransferKind.Export) => AnnotationExport,
            (_, ViewTransferKind.Export) => ConfigurationExport,
            (ViewArchiveContent.Annotations, _) => AnnotationImport,
            (ViewArchiveContent.Schemas, _) => SchemaImport,
            (ViewArchiveContent.Templates, _) => TemplateImport,
            _ => ConfigurationImport,
        };
    }

    // Throws AccessDeniedException when a scope of a kind of the include is not among the granted ones. The include is
    // resolved first: null or empty means all four kinds. isJob words the message for reading a stored job.
    public static void Ensure(IReadOnlySet<MachineScope> granted, IReadOnlyList<ViewArchiveContent>? include, ViewTransferKind kind, bool isJob = false)
    {
        // a sentence names every scope a kind needs, not only the missing ones, so it does not change with the
        // machine; the kinds that need the same scopes share it
        var missing = ViewArchiveInclude.Resolve(include)
            .Where(content => !GetScopes(content, kind).All(granted.Contains))
            .GroupBy(content => JoinAnd(GetScopes(content, kind).Select(scope => MachineScopes.ToScopeString(scope) ?? scope.ToString())))
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
            $"Missing scope for {JoinAnd(group.Select(content => content.ToString().ToLowerInvariant()))}{where}: {action} needs {group.Key}.");
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
