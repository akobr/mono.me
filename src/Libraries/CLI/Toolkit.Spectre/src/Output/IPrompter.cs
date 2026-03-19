using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace _42.CLI.Toolkit.Output;

public interface IPrompter
{
    T Input<T>(string message, T? defaultValue = default, IList<Func<object, ValidationResult>>? validators = null);

    bool Confirm(string message, bool? defaultValue = null);

    string Password(string message);

    IEnumerable<T> List<T>(string message);

    T Select<T>(string message, IEnumerable<T> choices, Func<T, string>? converter = null);

    IEnumerable<T> MultiSelect<T>(string message, IEnumerable<T> choices, Func<T, string>? converter = null, int? minimum = null, int? maximum = null);
}
