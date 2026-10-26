using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class RecordingUserTokenExchange : IUserTokenExchange
{
    public List<UserTokenExchangeRequest> Requests { get; } = [];

    public UserTokenExchangeResult Result { get; set; } =
        UserTokenExchangeResult.Success("storyteller-user-token", 600, "User.Impersonation", ["NoSecretReveal"]);

    public Task<UserTokenExchangeResult> ExchangeAsync(UserTokenExchangeRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Result);
    }
}
