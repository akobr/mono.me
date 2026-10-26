using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Owns its issuer and may return users, as the Storyteller token validator does.
internal sealed class RecordingUserTokenIssuerValidator : IUserTokenIssuerValidator
{
    private readonly string _issuer;

    public RecordingUserTokenIssuerValidator(string issuer)
    {
        _issuer = issuer;
    }

    public int Calls { get; private set; }

    public BearerValidationResult? Result { get; set; }

    public bool CanValidate(string issuer)
    {
        return OwnsIssuer(issuer);
    }

    public bool OwnsIssuer(string issuer)
    {
        return issuer == _issuer;
    }

    public Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
