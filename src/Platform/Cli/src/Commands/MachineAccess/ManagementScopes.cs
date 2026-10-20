using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Machine management scopes as the CLI accepts them: the enum name, the scope string (Configuration.Transfer) or kebab case
// (configuration-transfer), in any case. The SDK's enum, not the one of Abstractions.Access, is what the API takes.
internal static class ManagementScopes
{
    public const string Names = "ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage";

    // The values of --manage, comma-separated or repeated. Null when they name none: the request then leaves the member out
    // and the machine gets no management scope. A scope named twice counts once, and the scopes are in enum order, as the
    // server stores them.
    public static bool TryParse(IEnumerable<string>? values, out List<MachineManagementScope>? scopes, [NotNullWhen(false)] out string? unknown)
    {
        scopes = null;
        unknown = null;
        var parsed = new SortedSet<MachineManagementScope>();

        foreach (var name in (values ?? []).SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!TryParseName(name, out var scope))
            {
                unknown = name;
                return false;
            }

            parsed.Add(scope);
        }

        scopes = parsed.Count > 0 ? parsed.ToList() : null;
        return true;
    }

    // The string the scope puts into the credentials of the machine, as the API's Scopes constants spell it.
    public static string ToScopeString(MachineManagementScope scope)
    {
        return scope switch
        {
            MachineManagementScope.ConfigurationTransfer => "Configuration.Transfer",
            MachineManagementScope.SchemaWrite => "Schema.Write",
            MachineManagementScope.TemplateWrite => "Template.Write",
            MachineManagementScope.AnnotationManage => "Annotation.Manage",
            MachineManagementScope.KeyValueManage => "KeyValue.Manage",
            _ => scope.ToString(),
        };
    }

    // Without its dots and dashes the value must be a defined name; Enum.TryParse would also take numbers and comma-joined names.
    private static bool TryParseName(string name, out MachineManagementScope scope)
    {
        var compact = name.Replace(".", string.Empty).Replace("-", string.Empty);
        var match = Enum.GetNames<MachineManagementScope>().FirstOrDefault(candidate => candidate.Equals(compact, StringComparison.OrdinalIgnoreCase));
        scope = match is null ? default : Enum.Parse<MachineManagementScope>(match);
        return match is not null;
    }
}
