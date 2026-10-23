namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// Collects the external inputs that one resolve reads, the dependency manifest of the resolved document. A host puts it
/// on the <see cref="BindingScope"/>, and every <see cref="IRecordingBinding"/> on the way records what it reads.
/// A manifest is <see cref="IsVolatile"/> when the resolved document may depend on something the markers do not
/// capture, e.g. the clock or a binding that does not record. A volatile manifest must not stand in for the document.
/// </summary>
/// <remarks>
/// It fails closed: until a <see cref="BindingScope"/> carries it, nothing guarantees that the reads were recorded, so it
/// is volatile. Thread-safe.
/// </remarks>
public sealed class BindingDependencies
{
    private const string NotAttachedReason = "No resolve recorded its inputs.";

    private readonly object _lock = new();
    private readonly Dictionary<(string Kind, string Source, string Key), string> _markers = [];
    private string? _volatileReason;
    private bool _isAttached;

    public bool IsVolatile
    {
        get
        {
            lock (_lock)
            {
                return !_isAttached || _volatileReason is not null;
            }
        }
    }

    /// <summary>
    /// Why the manifest is volatile: the first reason given, or null when it is not.
    /// </summary>
    public string? VolatileReason
    {
        get
        {
            lock (_lock)
            {
                return _volatileReason ?? (_isAttached ? null : NotAttachedReason);
            }
        }
    }

    /// <summary>
    /// Records an input. The same input with the same marker again changes nothing. The same input with another marker
    /// makes the manifest volatile: two reads of one input saw different versions within one resolve.
    /// </summary>
    public void Record(BindingDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        lock (_lock)
        {
            var identity = (dependency.Kind, dependency.Source, dependency.Key);

            if (!_markers.TryAdd(identity, dependency.Marker)
                && !string.Equals(_markers[identity], dependency.Marker, StringComparison.Ordinal))
            {
                _volatileReason ??= $"The {dependency.Kind} input '{dependency.Key}' was read in two versions.";
            }
        }
    }

    /// <summary>
    /// Marks the manifest volatile. The first reason is kept.
    /// </summary>
    public void MarkVolatile(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (_lock)
        {
            _volatileReason ??= reason;
        }
    }

    /// <summary>
    /// The recorded inputs, sorted by kind, source and key (ordinal).
    /// </summary>
    public IReadOnlyList<BindingDependency> ToList()
    {
        lock (_lock)
        {
            return _markers
                .Select(entry => new BindingDependency(entry.Key.Kind, entry.Key.Source, entry.Key.Key, entry.Value))
                .OrderBy(dependency => dependency.Kind, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.Source, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.Key, StringComparer.Ordinal)
                .ToList();
        }
    }

    // A scope carries the collector into the resolve; see BindingScope.Dependencies.
    internal void Attach()
    {
        lock (_lock)
        {
            _isAttached = true;
        }
    }
}
