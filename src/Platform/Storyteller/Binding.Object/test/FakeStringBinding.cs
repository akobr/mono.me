using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

internal sealed class FakeStringBinding : IBindingExecutor
{
    public List<(string? Raw, string Path, bool IncludeSecrets, BindingScope? Scope)> Calls { get; } = new();

    public ValueTask<bool> TryBinding(JProperty property, bool includeSecrets, BindingScope? scope = null)
    {
        return BindAsync(property.Value, includeSecrets, scope, token => property.Value = token);
    }

    public ValueTask<bool> TryBinding(JValue value, bool includeSecrets, BindingScope? scope = null)
    {
        return BindAsync(value, includeSecrets, scope, token => value.Replace(token));
    }

    private ValueTask<bool> BindAsync(JToken current, bool includeSecrets, BindingScope? scope, Action<JToken> apply)
    {
        if (current.Type != JTokenType.String)
        {
            return new ValueTask<bool>(false);
        }

        var raw = current.Value<string>();
        Calls.Add((raw, current.Path, includeSecrets, scope));
        if (string.IsNullOrEmpty(raw) || raw[0] != '@')
        {
            return new ValueTask<bool>(false);
        }

        apply(new JValue("resolved:" + raw));
        return new ValueTask<bool>(true);
    }
}
