namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// A binding could not be evaluated because of its own content: a malformed envelope, an engine error, or a limit.
/// The API reports it as a client error.
/// </summary>
public class BindingEvaluationException : BindingException
{
    public BindingEvaluationException(string message)
        : base(message)
    {
    }

    public BindingEvaluationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BindingEvaluationException(string message, string? path, Exception? innerException)
        : base(message, innerException!)
    {
        Path = path;
    }

    /// <summary>
    /// Gets the JSON path of the binding that failed, when it is known.
    /// </summary>
    public string? Path { get; }
}
