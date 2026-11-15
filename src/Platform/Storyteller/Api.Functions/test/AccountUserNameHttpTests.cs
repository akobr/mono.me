using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// GET v1/access/account compares the token's user name with the stored one (AccountUserNameCheck), log only.
// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public class AccountUserNameHttpTests
{
    private const string ChangeMessage = "differs from the stored one";

    private readonly FederationAccessService _access = new();
    private readonly CapturingLogger<AccessHttp> _logger = new();

    public AccountUserNameHttpTests()
    {
        _access.Accounts["user-1"] = new Account
        {
            Id = "user-1",
            UserName = "ada@example.com",
            Name = "Ada Lovelace",
            AccessMap = new Dictionary<string, AccountRole> { ["acme"] = AccountRole.Owner },
        };
    }

    [Fact]
    public async Task GetAccount_AnotherUserNameInTheToken_IsLoggedAndTheAccountReturned()
    {
        await WithDecodingAsync(decodeWithoutValidation: false, async () =>
        {
            var result = await CreateHttp().GetAccount(UserRequest("mallory@example.com"));

            result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().UserName.ShouldBe("ada@example.com");
            var entry = _logger.Entries.Where(candidate => candidate.Message.Contains(ChangeMessage)).ShouldHaveSingleItem();
            entry.Level.ShouldBe(LogLevel.Warning);
            entry.Message.ShouldBe("The user name of the elevated account user-1 differs from the stored one at account: ada@example.com -> mallory@example.com (Administrator or Owner of acme)");
        });
    }

    [Fact]
    public async Task GetAccount_TheStoredUserName_IsNotLogged()
    {
        await WithDecodingAsync(decodeWithoutValidation: false, async () =>
        {
            await CreateHttp().GetAccount(UserRequest("Ada@Example.com"));

            _logger.Entries.ShouldNotContain(candidate => candidate.Message.Contains(ChangeMessage));
        });
    }

    // A delegated request carries the stored names, so it is never compared.
    [Fact]
    public async Task GetAccount_DelegatedRequest_IsNotChecked()
    {
        await WithDecodingAsync(decodeWithoutValidation: false, async () =>
        {
            var request = UserRequest("mallory@example.com");
            request.FunctionContext.Items[FunctionContextItemKeys.DelegatedActor] = "mcp";

            await CreateHttp().GetAccount(request);

            _logger.Entries.ShouldNotContain(candidate => candidate.Message.Contains(ChangeMessage));
        });
    }

    // DEV_AUTH decodes tokens without checking them: their names are no evidence.
    [Fact]
    public async Task GetAccount_TokensDecodedWithoutValidation_AreNotChecked()
    {
        await WithDecodingAsync(decodeWithoutValidation: true, async () =>
        {
            await CreateHttp().GetAccount(UserRequest("mallory@example.com"));

            _logger.Entries.ShouldNotContain(candidate => candidate.Message.Contains(ChangeMessage));
        });
    }

    private static async Task WithDecodingAsync(bool decodeWithoutValidation, Func<Task> body)
    {
        var previous = BearerValidationMode.DecodeWithoutValidation;
        BearerValidationMode.DecodeWithoutValidation = decodeWithoutValidation;

        try
        {
            await body();
        }
        finally
        {
            BearerValidationMode.DecodeWithoutValidation = previous;
        }
    }

    private static TestHttpRequestData UserRequest(string userName)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("scp", Scopes.User.Impersonation),
            new("preferred_username", userName),
        };
        return request;
    }

    private AccessHttp CreateHttp()
    {
        return new AccessHttp(_access, FunctionTestDoubles.ApiJsonOptions, _logger);
    }
}
