namespace _42.Testing.UI.Automation.Runtime;

// The test found the data in a state it did not create, e.g. a generated key that exists already. Thrown by the test or
// its page objects; the engine adapter never retries it, so an idempotency bug fails at once instead of passing on a
// second attempt with fresh data.
public sealed class DataConflictException : Exception
{
    public DataConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
