using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class RecordingBearerTokenValidator : IBearerTokenValidator
{
    public int Calls { get; private set; }

    public string? LastToken { get; private set; }

    public BearerValidationResult? Result { get; set; }

    public Exception? Error { get; set; }

    public Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastToken = rawToken;
        cancellationToken.ThrowIfCancellationRequested();

        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Result);
    }
}
