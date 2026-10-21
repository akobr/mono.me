namespace _42.Platform.Storyteller.Accessing;

// The catalogue of machine scopes: the enum names are the wire format, the scope strings are what claims and tokens
// carry (roles, scp, the Storyteller scope). A machine is exactly its stored set; nothing expands at request time.
public static class MachineScopes
{
    // Per-document reads and writes, in enum order.
    public static IReadOnlyList<MachineScope> Runtime { get; } =
    [
        MachineScope.AnnotationRead,
        MachineScope.AnnotationWrite,
        MachineScope.ConfigurationRead,
        MachineScope.ConfigurationWrite,
        MachineScope.SecretsRead,
        MachineScope.SecretsWrite,
    ];

    // Bulk and structural operations, on top of their runtime scope, in enum order.
    public static IReadOnlyList<MachineScope> Management { get; } =
    [
        MachineScope.ConfigurationTransfer,
        MachineScope.SchemaWrite,
        MachineScope.TemplateWrite,
        MachineScope.AnnotationManage,
        MachineScope.KeyValueManage,
    ];

    // Every scope, in enum order. Declared after Runtime and Management, which it is built from.
    public static IReadOnlyList<MachineScope> All { get; } = [.. Runtime, .. Management];

    // The one scope string of a scope; null for an undefined value.
    public static string? ToScopeString(MachineScope scope)
    {
        return scope switch
        {
            MachineScope.AnnotationRead => "Annotation.Read",
            MachineScope.AnnotationWrite => "Annotation.Write",
            MachineScope.ConfigurationRead => "Configuration.Read",
            MachineScope.ConfigurationWrite => "Configuration.Write",
            MachineScope.SecretsRead => "Secrets.Read",
            MachineScope.SecretsWrite => "Secrets.Write",
            MachineScope.ConfigurationTransfer => "Configuration.Transfer",
            MachineScope.SchemaWrite => "Schema.Write",
            MachineScope.TemplateWrite => "Template.Write",
            MachineScope.AnnotationManage => "Annotation.Manage",
            MachineScope.KeyValueManage => "KeyValue.Manage",
            _ => null,
        };
    }

    // Ordinal and exact: "annotation.read", an enum name or User.Impersonation are no scope strings.
    public static bool TryParseScopeString(string? value, out MachineScope scope)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(ToScopeString(candidate), value, StringComparison.Ordinal))
            {
                scope = candidate;
                return true;
            }
        }

        scope = default;
        return false;
    }

    public static bool IsRuntime(MachineScope scope)
    {
        return Runtime.Contains(scope);
    }

    public static bool IsManagement(MachineScope scope)
    {
        return Management.Contains(scope);
    }

    // Secret values need their own scope, and giving one needs ContributorWithSecrets.
    public static bool IsSecrets(MachineScope scope)
    {
        return scope is MachineScope.SecretsRead or MachineScope.SecretsWrite;
    }

    // The stored form: the defined values, distinct and in enum order; an empty list for none.
    public static IReadOnlyList<MachineScope> Normalize(IEnumerable<MachineScope>? scopes)
    {
        return scopes?.Where(Enum.IsDefined).Distinct().Order().ToList() ?? [];
    }

    // Normalize, plus X.Read for every X.Write of Annotation, Configuration and Secrets. Runs once, at creation.
    // Secrets imply no Configuration scope, and management scopes imply nothing.
    public static IReadOnlyList<MachineScope> WithImpliedReads(IEnumerable<MachineScope>? scopes)
    {
        var normalized = Normalize(scopes);
        var impliedReads = normalized.Select(GetImpliedRead).OfType<MachineScope>();
        return Normalize(normalized.Concat(impliedReads));
    }

    // The first value that names no scope (a JSON integer such as 99), or null.
    public static MachineScope? FindUndefined(IEnumerable<MachineScope>? scopes)
    {
        foreach (var scope in scopes ?? [])
        {
            if (!Enum.IsDefined(scope))
            {
                return scope;
            }
        }

        return null;
    }

    // The scope strings in the given order, each once; undefined values are skipped.
    public static IReadOnlyList<string> ToScopeStrings(IEnumerable<MachineScope> scopes)
    {
        return scopes
            .Distinct()
            .Select(ToScopeString)
            .OfType<string>()
            .ToList();
    }

    private static MachineScope? GetImpliedRead(MachineScope scope)
    {
        return scope switch
        {
            MachineScope.AnnotationWrite => MachineScope.AnnotationRead,
            MachineScope.ConfigurationWrite => MachineScope.ConfigurationRead,
            MachineScope.SecretsWrite => MachineScope.SecretsRead,
            _ => null,
        };
    }
}
