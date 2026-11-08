using System;
using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Cli.Configuration;

// The editor of $VISUAL, else of $EDITOR, as git and kubectl use them: the editor of sform when none is configured.
public sealed class EnvironmentEditor
{
    public const string VISUAL = "VISUAL";
    public const string EDITOR = "EDITOR";

    private EnvironmentEditor(string? variable, string? command)
    {
        Variable = variable;
        Command = command;
    }

    public static EnvironmentEditor None { get; } = new(null, null);

    // The variable that names the editor, VISUAL or EDITOR.
    public string? Variable { get; }

    public string? Command { get; }

    [MemberNotNullWhen(true, nameof(Variable), nameof(Command))]
    public bool IsSet => Command is not null;

    public static EnvironmentEditor FromEnvironment(Func<string, string?> getVariable)
    {
        foreach (var name in new[] { VISUAL, EDITOR })
        {
            var value = getVariable(name);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return new EnvironmentEditor(name, value.Trim());
            }
        }

        return None;
    }
}
