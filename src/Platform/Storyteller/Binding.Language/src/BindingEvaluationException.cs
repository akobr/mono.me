namespace _42.Platform.Storyteller.Binding.Language;

public sealed class BindingEvaluationException : BindingException
{
    public BindingEvaluationException(string message)
        : base(message)
    {
    }

    public BindingEvaluationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
