namespace _42.Platform.Storyteller.Accessing;

public sealed class BearerKeyRetrievalException : Exception
{
    public BearerKeyRetrievalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
