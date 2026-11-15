using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class AccountUserNameCheckTests
{
    private const string AccountId = "user_01";

    private readonly CapturingLogger<AccountUserNameCheckTests> _logger = new();

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("ADA@example.com")]
    [InlineData(" ada@example.com ")]
    public void SameUserName_IsNotLogged(string current)
    {
        Check(Account(" ada@example.com"), [new Claim("preferred_username", current)]).ShouldBeFalse();

        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void OtherUserName_IsLoggedWithBothNames()
    {
        Check(Account("ada@example.com"), [new Claim("sub", AccountId), new Claim("preferred_username", " mallory@example.com ")]).ShouldBeTrue();

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe("The user name of the account user_01 differs from the stored one at account: ada@example.com -> mallory@example.com");
        Value(entry, "AccountId").ShouldBe(AccountId);
        Value(entry, "Channel").ShouldBe("account");
        Value(entry, "StoredUserName").ShouldBe("ada@example.com");
        Value(entry, "TokenUserName").ShouldBe("mallory@example.com");
    }

    [Theory]
    [InlineData(AccountRole.Administrator)]
    [InlineData(AccountRole.Owner)]
    public void OtherUserNameOfAnElevatedAccount_NamesItsAccessPoints(AccountRole role)
    {
        var account = Account("ada@example.com", new Dictionary<string, AccountRole>
        {
            ["globex"] = role,
            ["acme.billing"] = AccountRole.Owner,
            ["acme"] = AccountRole.ContributorWithSecrets,
        });

        Check(account, [new Claim("preferred_username", "mallory@example.com")]).ShouldBeTrue();

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldStartWith("The user name of the elevated account user_01 differs from the stored one at account: ada@example.com -> mallory@example.com");
        Value(entry, "ElevatedAccessPoints").ShouldBe("acme.billing, globex");
    }

    [Fact]
    public void ElevatedAccountWithManyAccessPoints_NamesTheFirstOnes()
    {
        var map = Enumerable.Range(0, AccountUserNameCheck.MaxLoggedAccessPoints + 5).ToDictionary(index => $"org{index:D2}", _ => AccountRole.Owner);

        Check(Account("ada@example.com", map), [new Claim("preferred_username", "mallory@example.com")]).ShouldBeTrue();

        var points = Value(_logger.Entries.ShouldHaveSingleItem(), "ElevatedAccessPoints").ShouldBeOfType<string>();
        points.ShouldStartWith("org00, org01");
        points.ShouldEndWith("org19 and 5 more");
    }

    [Fact]
    public void AccountThatIsNotElevated_GetsThePlainMessage()
    {
        var account = Account("ada@example.com", new Dictionary<string, AccountRole> { ["acme"] = AccountRole.ContributorWithSecrets });

        Check(account, [new Claim("preferred_username", "mallory@example.com")]).ShouldBeTrue();

        _logger.Entries.ShouldHaveSingleItem().Message.ShouldStartWith("The user name of the account user_01");
    }

    // No evidence, no log.
    [Theory]
    [InlineData("ada@example.com", null)]
    [InlineData("ada@example.com", "")]
    [InlineData("ada@example.com", "  ")]
    [InlineData("", "mallory@example.com")]
    [InlineData(" ", "mallory@example.com")]
    [InlineData(AccountId, "mallory@example.com")]
    public void NoEvidence_IsNotLogged(string stored, string? current)
    {
        IReadOnlyList<Claim> claims = current is null ? [new Claim("sub", AccountId)] : [new Claim("preferred_username", current)];

        Check(Account(stored), claims).ShouldBeFalse();

        _logger.Entries.ShouldBeEmpty();
    }

    // As at registration: preferred_username, then unique_name, then upn; the first present one counts.
    [Fact]
    public void UserName_IsTakenInTheOrderOfRegistration()
    {
        AccountUserNameCheck.GetUserName([new Claim(ClaimTypes.Upn, "c"), new Claim("unique_name", "b"), new Claim("preferred_username", " a ")]).ShouldBe("a");
        AccountUserNameCheck.GetUserName([new Claim(ClaimTypes.Upn, "c"), new Claim("unique_name", "b")]).ShouldBe("b");
        AccountUserNameCheck.GetUserName([new Claim(ClaimTypes.Upn, "c")]).ShouldBe("c");
        AccountUserNameCheck.GetUserName([new Claim("email", "d")]).ShouldBeNull();
        AccountUserNameCheck.UserNameClaimTypes.ShouldBe(["preferred_username", "unique_name", ClaimTypes.Upn]);
    }

    private static Account Account(string userName, IReadOnlyDictionary<string, AccountRole>? accessMap = null)
    {
        return new Account
        {
            Id = AccountId,
            UserName = userName,
            Name = "Ada Lovelace",
            AccessMap = accessMap ?? new Dictionary<string, AccountRole>(),
        };
    }

    private static object? Value(CapturingLogger<AccountUserNameCheckTests>.Entry entry, string name)
    {
        return entry.Values.FirstOrDefault(pair => pair.Key == name).Value;
    }

    private bool Check(Account account, IReadOnlyList<Claim> claims)
    {
        return AccountUserNameCheck.LogIfChanged(_logger, account, claims, "account");
    }
}
