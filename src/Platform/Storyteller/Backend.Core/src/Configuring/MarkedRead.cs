namespace _42.Platform.Storyteller.Configuring;

// A read for a binding with the storage marker of what it read (see Binding.BindingDependency.Marker): the version
// of the stored item, in any state, or BindingDependency.Absent when nothing is stored. Marker is null when the
// service cannot tell, and the binding then makes the manifest of the resolve volatile.
public sealed record MarkedRead<T>(T? Value, string? Marker)
    where T : class;
