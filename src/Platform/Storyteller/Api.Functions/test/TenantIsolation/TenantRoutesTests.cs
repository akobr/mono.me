namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// The table of the tenant isolation tests holds every HTTP function of the API exactly once, so a new function fails here
// until it is classified, and every function that names a tenant (in its route, as an access point key or in its body) is
// tested by TenantIsolationHttpTests.
public class TenantRoutesTests
{
    [Fact]
    public void Table_HoldsEveryHttpFunctionOfTheApi_Once()
    {
        var table = TenantRoutes.All.Select(route => route.Function).ToList();

        table.ShouldBeUnique();
        table.Order(StringComparer.Ordinal).ShouldBe(TenantRoutes.Functions.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryFunctionThatNamesATenant_IsCheckedByTheApiOrHandedToAService()
    {
        foreach (var (name, function) in TenantRoutes.Functions)
        {
            var route = TenantRoutes.Get(name);
            var template = TenantRoutes.Trigger(function)!.Route!;

            if (template.Contains("{organization", StringComparison.Ordinal))
            {
                route.Address.ShouldBe(TenantAddress.Route, name);
            }
            else if (template.StartsWith("v1/access/points/{key", StringComparison.Ordinal))
            {
                route.Address.ShouldBe(TenantAddress.Key, name);
            }
            else
            {
                route.Address.ShouldBeOneOf([TenantAddress.Body, TenantAddress.None], name);
            }

            if (route.Address == TenantAddress.None)
            {
                route.Gate.ShouldBeOneOf([TenantGate.Caller, TenantGate.Anonymous], name);
            }
            else
            {
                route.Gate.ShouldBeOneOf([TenantGate.Api, TenantGate.Service], name);
            }
        }
    }

    // A function of a family is in its family's route shape: the family names one kind of resource.
    [Fact]
    public void EveryFamily_AddressesItsTenantOneWay()
    {
        foreach (var family in TenantRoutes.All.GroupBy(route => route.Family))
        {
            family.Select(route => route.Address).Distinct().ShouldHaveSingleItem(family.Key);
        }
    }
}
