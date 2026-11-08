namespace _42.Platform.Cli.AutomationTests.Coverage;

// An option with its forms in the order of its template ("-p", "--projectKey").
public sealed class CliOption
{
    public CliOption(IReadOnlyList<string> forms, string declaringType, string memberName, string valueType, string? enumType, IReadOnlyList<string>? enumNames)
    {
        Forms = forms;
        DeclaringType = declaringType;
        MemberName = memberName;
        ValueType = valueType;
        EnumType = enumType;
        EnumNames = enumNames;
    }

    public IReadOnlyList<string> Forms { get; }

    // The long form, else the first one: how coverage-values.json and the value items name the option.
    public string Name => Forms.FirstOrDefault(form => form.StartsWith("--", StringComparison.Ordinal)) ?? Forms[0];

    public string DeclaringType { get; }

    public string MemberName { get; }

    // The property's type as written in messages ("string?", "string[]", "KeyValueProtection").
    public string ValueType { get; }

    // The enum the option's type is (or holds, for arrays and nullables), with its member names; null otherwise.
    public string? EnumType { get; }

    public IReadOnlyList<string>? EnumNames { get; }

    // -p|--projectKey of BaseContextCommand.
    public bool IsContextProject => DeclaringType == CommandTree.ContextCommandType && Forms.Contains(CommandTree.ContextProjectForm, StringComparer.Ordinal);

    public override string ToString()
    {
        return string.Join('|', Forms);
    }
}
