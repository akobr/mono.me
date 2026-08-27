using _42.Platform.Storyteller.Binding.Language;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Binding;

public static class BindingsOptionsExtensions
{
    public static BindingsOptions AddSource<T>(
        this BindingsOptions @this,
        string key = BindingExecutor.DefaultSourceKey)
        where T : IBindingSource
    {
        @this.AddSource(provider => provider.GetRequiredService<T>(), key);
        return @this;
    }

    public static BindingsOptions AddFunction<T>(
        this BindingsOptions @this,
        string name)
        where T : IBindingFunction
    {
        @this.AddFunction(name, provider => provider.GetRequiredService<T>());
        return @this;
    }
}
