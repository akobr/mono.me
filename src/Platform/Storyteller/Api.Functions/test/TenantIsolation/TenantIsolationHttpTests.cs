using _42.Platform.Storyteller.Api.Security;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// With every organization in one container, the API's access checks are the only thing between tenants (424-242). Every
// function of a tenant is called by the callers of organization A (a user who is a member of A only, the same user through
// the MCP actor, and A's machines of every credential kind), through the real middlewares and handlers, against
// organization B, which has the same project and view names. Each refusal is paired with a control: the same request to A
// gets past the gate, so the refusal is the tenant's and not a broken request. A refusal is 401 or 403 with no service of
// the handler called. The role check runs as in Release in either build, and the bearer tokens are validated, not decoded.
// Dotted route values inside A's own organization are 424-241.
[Collection(nameof(BearerValidationMode))]
public class TenantIsolationHttpTests
{
    public static readonly TenantTarget A = new("A", TenantWorld.OrganizationA, TenantWorld.Project, $"{TenantWorld.OrganizationA}.{TenantWorld.Project}");

    // B by its name, by case variants of it, and by dotted values that contain it; none of them is A.
    public static readonly IReadOnlyList<TenantTarget> RoutesOfB =
    [
        new("B", TenantWorld.OrganizationB, TenantWorld.Project, TenantWorld.OrganizationB),
        new("B in upper case", "GLOBEX", TenantWorld.Project, TenantWorld.OrganizationB),
        new("B in mixed case", "Globex", "APP", TenantWorld.OrganizationB),
        new("B with a dotted project", TenantWorld.OrganizationB, "app.x", TenantWorld.OrganizationB),
        new("B with a dotted organization", "globex.app", "x", TenantWorld.OrganizationB),
        new("B behind the name of A", "acme.globex", TenantWorld.Project, TenantWorld.OrganizationB),
    ];

    // The access points of B as a {key} route or a body names them; a key is trimmed and lower-cased there.
    public static readonly IReadOnlyList<TenantTarget> KeysOfB =
    [
        new("organization B", TenantWorld.OrganizationB, TenantWorld.Project, "globex"),
        new("project of B", TenantWorld.OrganizationB, TenantWorld.Project, "globex.app"),
        new("project of B in upper case", TenantWorld.OrganizationB, TenantWorld.Project, "GLOBEX.APP"),
        new("project of B padded", TenantWorld.OrganizationB, TenantWorld.Project, " globex.app "),
        new("organization B in mixed case", TenantWorld.OrganizationB, TenantWorld.Project, "Globex"),
        new("a project named B in A", TenantWorld.OrganizationA, "globex", "acme.globex"),
    ];

    // The parameters of the services that name who acts, and those that name the tenant it acts in.
    private static readonly string[] CallerParameters = ["caller", "actorId"];

    private static readonly string[] TenantParameters = ["accessPointKey", "model", "organization"];

    public static TheoryData<string, TenantCaller> ApiFunctions => Matrix(TenantGate.Api);

    public static TheoryData<string, TenantCaller> ServiceFunctions => Matrix(TenantGate.Service);

    // A's control: the request to A gets past the gate (a machine on a function for users only is refused there too). B:
    // refused, and no service of the handler was called, for every name of B.
    [Theory]
    [MemberData(nameof(ApiFunctions))]
    public async Task ApiFunction_OfAnotherOrganization_IsRefusedBeforeAnyService(string function, TenantCaller caller)
    {
        var route = TenantRoutes.Get(function);
        var world = await TenantWorld.GetAsync();

        await WithValidatedTokensAsync(async () =>
        {
            var control = await TenantPipeline.InvokeAsync(world, route, caller, A);
            var machineOfUsersOnly = route.UsersOnly && TenantWorld.IsMachine(caller);

            if (machineOfUsersOnly)
            {
                control.ShouldSatisfyAllConditions(
                    () => control.Status.ShouldBe(403, $"{function} is for users only: {control}"),
                    () => control.Reached.ShouldBeEmpty());
            }
            else
            {
                // Past the gate: a service was reached, or the function answered with the one status its representative
                // request gets from its own check (TenantRoute.ControlStatus); any other answer is a fault of the request.
                control.ShouldSatisfyAllConditions(
                    () => control.IsRefused.ShouldBeFalse($"{caller} of A on {function} of A: {control}"),
                    () => (control.Reached.Count > 0 || control.Status == route.ControlStatus).ShouldBeTrue($"{caller} of A on {function} of A: {control}"));
            }

            var failures = new List<string>();

            foreach (var target in TargetsOfB(route))
            {
                var outcome = await TenantPipeline.InvokeAsync(world, route, caller, target);
                var expected = machineOfUsersOnly ? 403 : TenantWorld.IsMachine(caller) ? 401 : 403;

                if (outcome.Status != expected || outcome.Reached.Count > 0)
                {
                    failures.Add($"{target}: {outcome} (expected {expected} and nothing reached)");
                }
            }

            failures.ShouldBeEmpty($"{caller} of A on {function} of B");
        });
    }

    // Members, invitations, grants and the creation of access points: the access service decides by the caller's role on the
    // access point (CosmosAccessServiceTests, CosmosInvitationServiceTests). A machine is refused before it; a user's request
    // reaches it with the caller's own account and B's access point, never another identity. The renewal of a certificate
    // reaches the renewal service only for the certificate machine itself, with the route's organization
    // (AccessIsolationTests.CertificateRenewal_...); the token exchange of federated machines is anonymous and resolves the
    // client in the route's project.
    [Theory]
    [MemberData(nameof(ServiceFunctions))]
    public async Task ServiceFunction_OfAnotherOrganization_HandsTheCallerAndTheTenantToItsService(string function, TenantCaller caller)
    {
        var route = TenantRoutes.Get(function);
        var world = await TenantWorld.GetAsync();

        await WithValidatedTokensAsync(async () =>
        {
            var failures = new List<string>();

            foreach (var target in TargetsOfB(route).Prepend(A))
            {
                var outcome = await TenantPipeline.InvokeAsync(world, route, caller, target);
                var problem = ServiceProblem(route, caller, target, outcome);

                if (problem is not null)
                {
                    failures.Add($"{target}: {outcome} ({problem})");
                }
            }

            failures.ShouldBeEmpty($"{caller} of A on {function}");
        });
    }

    // The token exchange of federated machines takes no credentials: its service is asked for the route's project.
    [Fact]
    public async Task MachineToken_OfAnotherOrganization_IsExchangedInThatOrganizationOnly()
    {
        var route = TenantRoutes.Get(nameof(V1.FederationHttp.PostMachineToken));
        var world = await TenantWorld.GetAsync();

        await WithValidatedTokensAsync(async () =>
        {
            foreach (var target in RoutesOfB.Prepend(A))
            {
                var outcome = await TenantPipeline.InvokeAsync(world, route, caller: null, target);

                outcome.Reached.ShouldHaveSingleItem($"{target}: {outcome}");
                outcome.Reached[0].Values.ShouldContain(target.Organization, $"{target}: {outcome}");
                outcome.Reached[0].Values.ShouldContain(target.Project, $"{target}: {outcome}");
            }
        });
    }

    private static string? ServiceProblem(TenantRoute route, TenantCaller caller, TenantTarget target, TenantOutcome outcome)
    {
        if (route.Function == nameof(V1.CertificatesHttp.PostRenewCertificate))
        {
            if (caller != TenantCaller.CertificateMachine)
            {
                return outcome.Status == 401 && outcome.Reached.Count == 0 ? null : "only the certificate machine itself renews";
            }

            return outcome.Reached.Count == 1
                && outcome.Reached[0].Values.Contains(target.Organization)
                && outcome.Reached[0].Values.Contains(TenantWorld.CertificateMachineId)
                    ? null
                    : "the renewal service is asked for the route's organization and the machine itself";
        }

        if (TenantWorld.IsMachine(caller))
        {
            return outcome.Status == 403 && outcome.Reached.Count == 0 ? null : "a machine is refused before the service";
        }

        // An account registers once: the user of A has one, so the request ends before the service, whatever it names.
        if (route.Function == nameof(V1.AccessHttp.PostAccount))
        {
            return outcome.Status == 400 && outcome.Reached.Count == 0 ? null : "an account does not register again";
        }

        if (outcome.Reached.Count == 0)
        {
            return "the user's request reaches the service";
        }

        // The caller and the tenant at their own parameters, so a call that swaps the caller with the account it acts on fails.
        var callers = outcome.Reached.SelectMany(call => CallerParameters.SelectMany(call.Of)).ToList();

        if (!callers.Contains(TenantWorld.UserA) || callers.Contains(TenantWorld.UserB))
        {
            return "the service gets the caller's own account as its caller";
        }

        // A {key} is trimmed and lower-cased by the API; a body is handed over as it is.
        string[] tenants = route.Address switch
        {
            TenantAddress.Key => [target.Key.Trim().ToLowerInvariant()],
            TenantAddress.Body => [target.Key, target.Organization],
            _ => [target.Organization],
        };
        var values = outcome.Reached.SelectMany(call => TenantParameters.SelectMany(call.Of)).ToList();
        return values.Any(value => tenants.Contains(value, StringComparer.Ordinal)) ? null : $"the service gets the tenant '{string.Join("' or '", tenants)}'";
    }

    private static IReadOnlyList<TenantTarget> TargetsOfB(TenantRoute route)
    {
        return route.Address == TenantAddress.Route ? RoutesOfB : KeysOfB;
    }

    private static TheoryData<string, TenantCaller> Matrix(TenantGate gate)
    {
        var data = new TheoryData<string, TenantCaller>();

        foreach (var route in TenantRoutes.All.Where(route => route.Gate == gate && route.Function != nameof(V1.FederationHttp.PostMachineToken)))
        {
            foreach (var caller in TenantWorld.Callers)
            {
                data.Add(route.Function, caller);
            }
        }

        return data;
    }

    private static async Task WithValidatedTokensAsync(Func<Task> body)
    {
        var previous = BearerValidationMode.DecodeWithoutValidation;
        BearerValidationMode.DecodeWithoutValidation = false;

        try
        {
            await body();
        }
        finally
        {
            BearerValidationMode.DecodeWithoutValidation = previous;
        }
    }
}
