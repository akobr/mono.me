using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.DbCreator.Configuration;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The account that owns what a seed creates: the base account (baseAccount:id) with its user name and name, or any
// other account id, which is its own user name and name until its person signs in.
public sealed record class DataSetOwner(string Id, string UserName, string Name)
{
    // The given id, or the base account without one. DataSetException for an id the API never registers: with ':' it
    // is an Entra ID account id, {tid}:{oid} in lower case.
    public static DataSetOwner Resolve(string? ownerId, BaseAccountOptions baseAccount)
    {
        var baseAccountId = baseAccount.GetAccountId();
        var id = string.IsNullOrWhiteSpace(ownerId) ? baseAccountId : ownerId.Trim();

        if (id.Contains(':') && !UserIdentity.IsEntraAccountId(id))
        {
            throw new DataSetException(
                $"'{id}' is not an account id. Under Entra ID it is the owner's {{tid}}:{{oid}}, two lower-case GUIDs; under AuthKit or Keycloak the sub, without ':'.");
        }

        return id == baseAccountId
            ? new DataSetOwner(id, Constants.BASE_ACCOUNT_USER_NAME, Constants.BASE_ACCOUNT_NAME)
            : new DataSetOwner(id, id, id);
    }
}
