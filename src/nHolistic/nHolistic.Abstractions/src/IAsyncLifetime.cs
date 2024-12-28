namespace _42.tHolistic;

/// <summary>
/// Used to provide asynchronous lifetime functionality.
/// </summary>
public interface IAsyncLifetime : IAsyncDisposable
{
    /// <summary>
    /// Called immediately after the class has been created, before it is used.
    /// </summary>
    ValueTask InitializeAsync();
}
