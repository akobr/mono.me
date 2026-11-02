using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "New machine access" (MachineCreateDialog.vue), opened by "New machine" of the Machines page: "For <org>/<project>.",
// the group "Presets" (Runtime reader, Runtime with secrets, CI writer, Git sync, Backup, Schema deployer; a preset the
// user cannot give is disabled), the checkboxes of "Runtime scopes" and "Management scopes" named by their scope
// strings (Annotation.Read, …; management scopes below Administrator and secrets scopes below ContributorWithSecrets
// are disabled, with a help text), "Annotation key" (an input menu of the default view's catalog, "Any annotation"),
// "Credential kind" (the policy's kinds by their API names, disabled with fewer than two), for CertificateAndApiKey
// "Use a shared certificate of the project", "Certificate lifetime (days)" for a machine with its own certificate, and
// for Federated the rules (FederationRuleGroup) or, without a trusted issuer, the alert "No trusted issuers". Every
// open starts again from Runtime reader and the policy's default kind. Checking X.Write checks X.Read, unchecking
// X.Read unchecks X.Write. Create posts the machine and closes the dialog: the credentials follow once
// (OneTimeSecretDialog "Machine access created"), or the setup of a Federated machine (FederatedSetupDialog); an API
// refusal is an alert in the dialog.
public sealed partial class MachineCreateDialog(App app, AppRoutes routes)
{
    // The refusal of a deployment that does not support Federated machines (501 MachineAccessNotConfigured).
    public const string FederationNotConfigured = "Federated machine access is not configured for this deployment.";

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New machine access", Exact = true });

    // The dialog's description, its accessible description.
    public string DescriptionText => $"For {routes.Organization}/{routes.Project}.";

    public ILocator Presets => Root.GetByRole(AriaRole.Group, new() { Name = "Presets", Exact = true });

    public ILocator PresetsHint => Presets.GetByText("Replace the scopes below with those of a typical machine.", new() { Exact = true });

    public ILocator NoRuntimeScope => Root.GetByText("Pick at least one runtime scope.", new() { Exact = true });

    // The help below Administrator.
    public ILocator ManagementScopesHelp => Root.GetByText("Only Administrators give management scopes.", new() { Exact = true });

    // The help below ContributorWithSecrets.
    public ILocator SecretsScopesHelp => Root.GetByText("Only users with secret rights give secrets scopes.", new() { Exact = true });

    // The input menu of "Annotation key"; its options are the default view's keys (virtualized).
    public ILocator AnnotationKey => Root.GetByPlaceholder("Any annotation", new() { Exact = true });

    public ILocator AnnotationKeyHelp => Root.GetByText("Optional: limits the machine to this annotation and its descendants.", new() { Exact = true });

    // The options of the open "Annotation key" menu (a listbox outside the dialog).
    public ILocator AnnotationKeyOptions => app.Page.GetByRole(AriaRole.Listbox).GetByRole(AriaRole.Option);

    public ILocator CredentialKind => Root.GetByRole(AriaRole.Combobox, new() { Name = "Credential kind", Exact = true });

    // The help under "Credential kind": the description of the picked kind.
    public ILocator CredentialKindHelp => NuxtLocators.FieldHelp(Root, "Credential kind");

    public ILocator UseSharedCertificate => Root.GetByRole(AriaRole.Checkbox, new() { Name = "Use a shared certificate of the project", Exact = true });

    public ILocator CertificateLifetime => Root.GetByLabel("Certificate lifetime (days)", new() { Exact = true });

    // The alert of a Federated machine in a project without a trusted issuer.
    public ILocator NoTrustedIssuers => NuxtLocators.Alert(Root, "No trusted issuers");

    // What NoTrustedIssuers asks for: to add one (an Administrator), or to ask an Administrator (a Contributor).
    public ILocator NoTrustedIssuersHint => NuxtLocators.AlertDescription(Root, "No trusted issuers");

    // The action of NoTrustedIssuers for an Administrator: the link to machines#trusted-issuers.
    public ILocator AddTrustedIssuerLink => Root.GetByRole(AriaRole.Link, new() { Name = "Add a trusted issuer", Exact = true });

    public ILocator AddRuleButton => Root.GetByRole(AriaRole.Button, new() { Name = "Add rule", Exact = true });

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    public ILocator CreateButton => Root.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true });

    // A preset button by its exact name.
    public ILocator Preset(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Presets.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    }

    // A scope checkbox by its scope string: Annotation.Read, Annotation.Write, Configuration.Read, Configuration.Write,
    // Secrets.Read, Secrets.Write, Configuration.Transfer, Schema.Write, Template.Write, Annotation.Manage or
    // KeyValue.Manage.
    public ILocator Scope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return Root.GetByRole(AriaRole.Checkbox, new() { Name = scope, Exact = true });
    }

    // An option of the open "Credential kind" select by its API name (exact: Certificate is a prefix of
    // CertificateAndApiKey).
    public ILocator CredentialKindOption(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = kind, Exact = true });
    }

    // A rule of a Federated machine: the group "Rule <n>".
    public FederationRuleGroup Rule(int number)
    {
        return new FederationRuleGroup(Root, number);
    }

    // The error alert of a refused Create, by the API's message.
    public ILocator Failure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return NuxtLocators.Message(Root, message);
    }

    public async Task WaitForOpenAsync()
    {
        await Expect(Root).ToBeVisibleAsync();
        await Expect(Root).ToHaveAccessibleDescriptionAsync(DescriptionText);
    }

    // Picks the credential kind (its API name) unless it is picked already. The select shows once the policy loaded;
    // it is disabled when the policy enables one kind only, which must then be the kind.
    public async Task PickCredentialKindAsync(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        await Expect(CredentialKind).ToBeVisibleAsync();
        if (string.Equals((await CredentialKind.InnerTextAsync()).Trim(), kind, StringComparison.Ordinal))
        {
            return;
        }

        if (await CredentialKind.IsDisabledAsync())
        {
            throw new InvalidOperationException($"The machine policy of {routes.Organization}/{routes.Project} enables one credential kind only, which is not {kind}.");
        }

        await CredentialKind.ClickAsync();
        await CredentialKindOption(kind).ClickAsync();
        await Expect(CredentialKind).ToHaveTextAsync(kind);
    }

    // Clicks Create and returns the credentials' dialog "Machine access created" once it showed (not for Federated).
    public async Task<OneTimeSecretDialog> CreateAsync()
    {
        await CreateButton.ClickAsync();
        var secret = new OneTimeSecretDialog(app, OneTimeSecretDialog.CreatedTitle);
        await Expect(secret.Root).ToBeVisibleAsync();
        await Expect(Root).ToBeHiddenAsync();
        return secret;
    }

    // Clicks Create of a Federated machine and returns its setup dialog "Federated machine created" once it showed. A
    // deployment without Federated machine access refuses it (FederationNotConfigured): the test is skipped then.
    public async Task<FederatedSetupDialog> CreateFederatedAsync()
    {
        await CreateButton.ClickAsync();
        var setup = new FederatedSetupDialog(app, FederatedSetupDialog.CreatedTitle);
        var notConfigured = Failure(FederationNotConfigured);
        await Expect(setup.Root.Or(notConfigured)).ToBeVisibleAsync();
        Skip.When(await notConfigured.IsVisibleAsync(), FederationNotConfigured);
        await Expect(Root).ToBeHiddenAsync();
        return setup;
    }

    // The setup of a test that acts on a Federated machine: opens New machine, picks Federated, keeps the preset
    // scopes and the trusted issuer that Rule 1 preselects (the project has exactly this one) with the prefilled
    // audience, types the subject, creates the machine and closes its setup with Done. Returns the machine's id once
    // its row is listed. Skipped on a deployment without Federated machine access. created gets the id as soon as the
    // machine exists, before the steps that may still fail, so that the test's cleanup deletes it.
    public async Task<string> CreateFederatedMachineAsync(MachinesPage machines, string issuer, string subject, Action<string>? created = null)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        await machines.OpenCreateDialogAsync();
        await PickCredentialKindAsync("Federated");
        var rule = Rule(1);
        await Expect(rule.TrustedIssuer).ToHaveTextAsync(issuer);
        await rule.Subject.FillAsync(subject);
        var setup = await CreateFederatedAsync();
        var id = await setup.MachineIdAsync();
        created?.Invoke(id);
        await setup.DoneAsync();
        await Expect(machines.MachineRow(id)).ToBeVisibleAsync();
        return id;
    }
}
