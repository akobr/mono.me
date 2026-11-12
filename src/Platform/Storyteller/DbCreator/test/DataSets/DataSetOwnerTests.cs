using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

public class DataSetOwnerTests
{
    private const string EntraId = "4b0ac1a1-3d5f-4d0c-9a3b-1f2e3d4c5b6a:0c1d2e3f-4a5b-6c7d-8e9f-0a1b2c3d4e5f";

    [Fact]
    public void Resolve_IsTheBaseAccountWithoutAnId()
    {
        var owner = DataSetOwner.Resolve(null, new BaseAccountOptions { Id = " user_01base " });

        owner.ShouldBe(new DataSetOwner("user_01base", Constants.BASE_ACCOUNT_USER_NAME, Constants.BASE_ACCOUNT_NAME));
        DataSetOwner.Resolve(" ", new BaseAccountOptions()).Id.ShouldBe(Constants.LEGACY_BASE_ACCOUNT_ID);
        DataSetOwner.Resolve("user_01base", new BaseAccountOptions { Id = "user_01base" }).Name.ShouldBe(Constants.BASE_ACCOUNT_NAME);
    }

    [Fact]
    public void Resolve_NamesAnotherAccountByItsId()
    {
        DataSetOwner.Resolve(" user_02other ", new BaseAccountOptions { Id = "user_01base" })
            .ShouldBe(new DataSetOwner("user_02other", "user_02other", "user_02other"));
        DataSetOwner.Resolve(EntraId, new BaseAccountOptions()).Id.ShouldBe(EntraId);
    }

    [Fact]
    public void Resolve_RefusesAnIdTheApiNeverRegisters()
    {
        Should.Throw<DataSetException>(() => DataSetOwner.Resolve("tenant:object", new BaseAccountOptions()))
            .Message.ShouldContain("'tenant:object' is not an account id");
        Should.Throw<DataSetException>(() => DataSetOwner.Resolve(null, new BaseAccountOptions { Id = EntraId.ToUpperInvariant() }))
            .Message.ShouldContain("is not an account id");
    }
}
