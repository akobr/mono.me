using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class RecordingMachineTokenValidator : IMachineTokenValidator
{
    private readonly string _issuer;

    public RecordingMachineTokenValidator(string issuer)
    {
        _issuer = issuer;
    }

    public int Calls { get; private set; }

    public BearerValidationResult? Result { get; set; }

    public Exception? Error { get; set; }

    public bool CanValidate(string issuer)
    {
        return issuer == _issuer;
    }

    public Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Error is null ? Task.FromResult(Result) : throw Error;
    }
}
