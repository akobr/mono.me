using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Binding;

public static class BindingsOptionsExtensions
{
    public static BindingsOptions AddStrategy<T>(this BindingsOptions @this, string key = BindingService.DefaultBindingKey) where T : IBindingStrategy
    {
        @this.AddStrategy(provider => provider.GetRequiredService<IBindingStrategy>(), key);
        return @this;
    }
}
