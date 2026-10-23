namespace _42.Platform.Storyteller.Sdk.Configuring.Reading;

// What a read of one document came to. The failures carry their class: Definitive fails a startup even of an optional
// provider, Transient falls back to the cache and keeps polling, Withheld fails loudly.
internal abstract record ReadOutcome
{
    private ReadOutcome()
    {
    }

    // A 200: the server's document, before any local resolution.
    public sealed record Changed(Configuration Document, string? ETag, string? CacheControl) : ReadOutcome;

    // A 304. ETag is the answer's tag, or the one sent when the answer had none.
    public sealed record NotModified(string? ETag, string? CacheControl) : ReadOutcome;

    public abstract record Failure : ReadOutcome
    {
        private protected Failure(string message, int? statusCode, string? errorCode, bool isTransient, Exception? exception)
        {
            Message = message;
            StatusCode = statusCode;
            ErrorCode = errorCode;
            IsTransient = isTransient;
            Exception = exception;
        }

        // Names the key, the status and the fix; never a secret.
        public string Message { get; }

        public int? StatusCode { get; }

        public string? ErrorCode { get; }

        public bool IsTransient { get; }

        public Exception? Exception { get; }

        public StorytellerConfigurationException ToException(string key)
        {
            return new StorytellerConfigurationException(Message, key, StatusCode, ErrorCode, IsTransient, Exception);
        }
    }

    // X-2S-Secrets-Withheld on a resolved read: the machine may read secrets but presented no certificate.
    public sealed record Withheld : Failure
    {
        public Withheld(string reason, int statusCode, string message)
            : base(message, statusCode, null, false, null)
        {
            Reason = reason;
        }

        // The header's value, mtls-required.
        public string Reason { get; }
    }

    // 400, 401, 403, 404, 422, 501 and the other answers a retry would not change; a credential that is misconfigured
    // or refused.
    public sealed record Definitive : Failure
    {
        public Definitive(int? statusCode, string? errorCode, string message, Exception? exception)
            : base(message, statusCode, errorCode, false, exception)
        {
        }
    }

    // A network error, a timeout, an open circuit, 408, 424, 429 and 5xx other than 501; an unavailable token endpoint.
    public sealed record Transient : Failure
    {
        public Transient(int? statusCode, string? errorCode, string message, Exception? exception)
            : base(message, statusCode, errorCode, true, exception)
        {
        }
    }
}
