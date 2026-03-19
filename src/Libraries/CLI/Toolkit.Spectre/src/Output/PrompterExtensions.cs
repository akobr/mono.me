using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace _42.CLI.Toolkit.Output;

public static class PrompterExtensions
{
    public static bool Confirm(this IPrompter prompter, string message, bool? defaultValue = null)
    {
        return prompter.Confirm(message, defaultValue);
    }

    public static TValue Input<TValue>(
        this IPrompter prompter,
        string message,
        object? defaultValue = null,
        IList<Func<object, ValidationResult>>? validators = null)
    {
        var typedDefault = defaultValue is TValue typed ? typed : default;
        return prompter.Input<TValue>(message, typedDefault, validators);
    }
}
