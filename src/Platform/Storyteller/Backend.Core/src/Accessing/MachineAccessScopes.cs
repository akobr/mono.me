namespace _42.Platform.Storyteller.Accessing;

// The Storyteller scopes each machine access scope grants. Machine credentials carry these as roles.
public static class MachineAccessScopes
{
    public static IReadOnlyList<string> Get(MachineAccessScope scope)
    {
        return scope switch
        {
            MachineAccessScope.AnnotationRead => ["Annotation.Read"],
            MachineAccessScope.AnnotationReadWrite => ["Annotation.Read", "Annotation.ReadWrite"],
            MachineAccessScope.ConfigurationRead => ["Configuration.Read"],
            MachineAccessScope.ConfigurationReadWrite => ["Configuration.Read", "Configuration.ReadWrite"],
            MachineAccessScope.DefaultRead => ["Default.Read", "Annotation.Read", "Configuration.Read"],
            MachineAccessScope.DefaultReadWrite => ["Default.Read", "Default.ReadWrite", "Annotation.Read", "Annotation.ReadWrite", "Configuration.Read", "Configuration.ReadWrite"],
            MachineAccessScope.SecretsRead => ["Configuration.Read", "Configuration.Secrets"],
            MachineAccessScope.SecretsReadWrite => ["Configuration.Read", "Configuration.ReadWrite", "Configuration.Secrets"],
            _ => [],
        };
    }

    // The one scope string of a management scope; null for an undefined value.
    public static string? Get(MachineManagementScope scope)
    {
        return scope switch
        {
            MachineManagementScope.ConfigurationTransfer => "Configuration.Transfer",
            MachineManagementScope.SchemaWrite => "Schema.Write",
            MachineManagementScope.TemplateWrite => "Template.Write",
            MachineManagementScope.AnnotationManage => "Annotation.Manage",
            MachineManagementScope.KeyValueManage => "KeyValue.Manage",
            _ => null,
        };
    }

    // Everything a machine's credentials carry: the preset's scopes first, then its management scopes in enum order.
    public static IReadOnlyList<string> Get(MachineAccessScope scope, IEnumerable<MachineManagementScope>? managementScopes)
    {
        var management = Normalize(managementScopes);

        if (management is null)
        {
            return Get(scope);
        }

        return Get(scope)
            .Concat(management.Select(managementScope => Get(managementScope)!))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // The stored form: the defined values, distinct and in enum order, or null when none is left.
    public static IReadOnlyList<MachineManagementScope>? Normalize(IEnumerable<MachineManagementScope>? scopes)
    {
        var normalized = scopes?.Where(Enum.IsDefined).Distinct().Order().ToList();
        return normalized is { Count: > 0 } ? normalized : null;
    }

    // The first value that names no management scope (a JSON integer such as 99), or null.
    public static MachineManagementScope? FindUndefined(IEnumerable<MachineManagementScope>? scopes)
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
}
