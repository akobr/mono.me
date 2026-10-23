namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// A binding source, function, executor or resolver that records every external input it reads into the
/// <see cref="BindingDependencies"/> of the request or scope, or reads none, and passes the collector on. Optional:
/// a component without it makes the manifest of a resolve volatile, so a new binding cannot make a host trust a
/// manifest that misses its reads.
/// </summary>
public interface IRecordingBinding
{
}
