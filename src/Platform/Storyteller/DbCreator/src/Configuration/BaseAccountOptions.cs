using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.DbCreator.Configuration;

// The account seeded as the owner of the organization and its default project (baseAccount:id, or the environment
// variable baseAccount__id). It is the id the API keys the owner's account by: the lower-case {tid}:{oid} under Entra
// ID, the sub under AuthKit or Keycloak.
public class BaseAccountOptions
{
    public const string SECTION = "baseAccount";

    public string? Id { get; set; }

    // The configured id, or the legacy default when none is set.
    public string GetAccountId()
    {
        return string.IsNullOrWhiteSpace(Id) ? Constants.LEGACY_BASE_ACCOUNT_ID : Id.Trim();
    }

    public bool UsesLegacyAccountId()
    {
        return string.IsNullOrWhiteSpace(Id);
    }

    // An id with ':' is meant as an Entra ID account id, which the API registers only as {guid}:{guid} in lower case.
    public bool HasValidAccountId()
    {
        var accountId = GetAccountId();
        return !accountId.Contains(':') || UserIdentity.IsEntraAccountId(accountId);
    }
}
