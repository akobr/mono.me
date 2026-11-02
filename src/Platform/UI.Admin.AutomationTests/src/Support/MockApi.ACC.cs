using System.Text.Json;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The Access section's calls of the mock API: window.storytellerMock.addMachine, which adds a machine to the project of
// the routes as its Owner would (any scopes, any credential kind), e.g. one a Contributor may neither reset nor delete.
// The page shows it after its next load (App.GotoAsync keeps the tab's data).
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
}
