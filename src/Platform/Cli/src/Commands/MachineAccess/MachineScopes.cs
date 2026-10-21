using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Machine scopes as the CLI accepts them: the enum name, the scope string (Secrets.Read) or kebab case (secrets-read),
// in any case. The SDK's enum, not the one of Abstractions.Access, is what the API takes.
internal static class MachineScopes
{
    public const string Names = "AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead, SecretsWrite, ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage";

    public const string ManagementNames = "ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage";

    // -r, and the scopes a machine gets when no option gives it a runtime scope.
    public static IReadOnlyList<MachineScope> ReadOnly { get; } = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead];

    // -w: the reads and the writes of annotations and configurations, no secrets.
    public static IReadOnlyList<MachineScope> Write { get; } =
        [MachineScope.AnnotationRead, MachineScope.AnnotationWrite, MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite];

    // The values of --scope or --manage, comma-separated or repeated; empty when they name none. A scope named twice
    // counts once, and the scopes are in enum order, as the server stores them.
    public static bool TryParse(IEnumerable<string>? values, out SortedSet<MachineScope> scopes, [NotNullWhen(false)] out string? unknown)
    {
        scopes = [];
        unknown = null;

        foreach (var name in (values ?? []).SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!TryParseName(name, out var scope))
            {
                unknown = name;
                return false;
            }

            scopes.Add(scope);
        }

        return true;
    }

    // The string the scope puts into the credentials of the machine, as the API's Scopes constants spell it.
    public static string ToScopeString(MachineScope scope)
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
            _ => scope.ToString(),
        };
    }

    // Bulk and structural operations, on top of their runtime scope; every other scope is a runtime scope.
    public static bool IsManagement(MachineScope scope)
    {
        return scope is MachineScope.ConfigurationTransfer
            or MachineScope.SchemaWrite
            or MachineScope.TemplateWrite
            or MachineScope.AnnotationManage
            or MachineScope.KeyValueManage;
    }

    // Without its dots and dashes the value must be a defined name; Enum.TryParse would also take numbers and comma-joined names.
    private static bool TryParseName(string name, out MachineScope scope)
    {
        var compact = name.Replace(".", string.Empty).Replace("-", string.Empty);
        var match = Enum.GetNames<MachineScope>().FirstOrDefault(candidate => candidate.Equals(compact, StringComparison.OrdinalIgnoreCase));
        scope = match is null ? default : Enum.Parse<MachineScope>(match);
        return match is not null;
    }
}
