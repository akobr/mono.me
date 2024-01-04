using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Sharprompt;

namespace _42.CLI.Toolkit.Output;

public static class PrompterExtensions
{
    public static bool Confirm(this IPrompter prompter, string message, bool? defaultValue = null)
    {
        return prompter.Confirm(new ConfirmOptions()
        {
            Message = message,
            DefaultValue = defaultValue,
        });
    }

    public static TValue Input<TValue>(
        this IPrompter prompter,
        string message,
        object? defaultValue = null,
        IList<Func<object, ValidationResult>>? validators = null)
    {
        InputOptions<TValue> options = new()
        {
            Message = message,
            DefaultValue = defaultValue,
        };

        options.Validators.Merge(validators);
        return prompter.Input<TValue>(options);
    }
}
