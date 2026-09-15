namespace _42.Platform.Storyteller.Configuring;

/// <summary>
/// The concrete type placed into <see cref="_42.Platform.Storyteller.Binding.BindingScope.Context"/> while resolving
/// a configuration, letting binding functions such as <c>@annotation</c> identify which configuration is currently
/// being resolved.
/// </summary>
public sealed record ConfigurationBindingContext(FullKey ConfigurationKey);
