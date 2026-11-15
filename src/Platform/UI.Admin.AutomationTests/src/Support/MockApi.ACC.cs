using System.Text.Json;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The Access section's calls of the mock API: window.storytellerMock.addMachine, which adds a machine to the project of
// the routes as its Owner would (any scopes, any credential kind), e.g. one a Contributor may neither reset nor delete,
// and window.storytellerMock.setExhibition, which sets the deployment's exhibition (the demo project that onboarding
// offers to join) or turns it off. The page shows either after its next load (App.GotoAsync keeps the tab's data).
public sealed partial class MockApi
{
    // machine: a MachineAccess as the API stores it, serialized as JSON with its own property names (Id, ObjectId,
    // Scopes as enum names such as "ConfigurationTransfer", CredentialKind, FederationRules).
    public async Task AddMachineAsync(object machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        await WaitForMockAsync("!!window.storytellerMock", "window.storytellerMock");
        var argument = JsonSerializer.Serialize(new
        {
            projectKey = $"{routes.Organization}.{routes.Project}",
            machine,
        });
        await app.Page.EvaluateAsync(
            "argument => { const { projectKey, machine } = JSON.parse(argument); window.storytellerMock.addMachine(projectKey, machine); }",
            argument);
    }

    // exhibition: the organization and the project of the deployment's exhibition (the seed's is exhibition/exhibition),
    // or null for a deployment without one, whatever the routes. The onboarding wizard asks for the exhibition once per
    // page load, so load the wizard first (the mock has started once it shows its steps), then call this and open the
    // wizard again. In the new-user scenario the MockApi is one of the test's own App.
    public async Task SetExhibitionAsync((string Organization, string Project)? exhibition)
    {
        await WaitForMockAsync("!!window.storytellerMock", "window.storytellerMock");
        var argument = JsonSerializer.Serialize(exhibition is { } value ? new { value.Organization, value.Project } : null);
        await app.Page.EvaluateAsync(
            "argument => { window.storytellerMock.setExhibition(JSON.parse(argument)); }",
            argument);
    }
}
