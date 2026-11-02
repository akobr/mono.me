using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// Federation: trusted issuers (the templates, their validation, the JWKS URI and Pasted JWKS key sources, edit and the
// typed delete) and Federated machines (the path from no trusted issuer, the rule editor, the setup dialog, and the
// rules edited on the detail), each in a disposable project: trusted issuers are a project-wide list, and Federated
// needs a change of the machine policy. A deployment without Federated machine access refuses the machine's Create,
// and the test is skipped with that message. The cleanup deletes the machines a test created, then its trusted issuers.
[Area(Area.Access)]
public sealed partial class FederationTests : PlatformTest
{
    private const string GitHubIssuer = "https://token.actions.githubusercontent.com";
    private const string RepositoryOwnerId = "123456";

    // The machines and the trusted issuers a test created (in the project of its routes), deleted in CleanUpAsync
    // while listed.
    private readonly List<(AppRoutes Routes, string Id)> createdMachines = [];
    private readonly List<(AppRoutes Routes, string Name)> addedIssuers = [];

    // ACC-FED-01: Add, edit and delete a trusted issuer from the GitHub template.
    // A new project has no trusted issuer (and Federated disabled); the dialog opens with the GitHub Actions template
    // and needs a value of repository_owner_id; the issuer is listed with its key source, lifetime and claim, its edit
    // keeps the name and the issuer and saves a description, and the typed delete removes it.
    [Test]
    [TestCase(204)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task TrustedIssuers_AddEditDelete_UpdatesList()
    {
        var project = await CreateDisposableProjectAsync();
        var name = PlatformData.TrustedIssuerName();
        var saved = $"Trusted issuer {name} saved";

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        var issuers = machines.TrustedIssuers;
        await issuers.WaitForIssuersAsync();
        await Expect(issuers.FederationDisabled).ToBeVisibleAsync();
        await Expect(issuers.NoTrustedIssuers).ToBeVisibleAsync();

        var add = await issuers.OpenAddDialogAsync();
        await Expect(add.Root).ToHaveAccessibleDescriptionAsync(TrustedIssuerDialog.DescriptionText);
        await Expect(add.Template).ToHaveTextAsync("GitHub Actions");
        await Expect(add.Name).ToHaveValueAsync("github");
        await Expect(add.Issuer).ToHaveValueAsync(GitHubIssuer);
        await Expect(add.ClaimName(1)).ToHaveValueAsync("repository_owner_id");
        await Expect(add.ClaimError("Enter the accepted value of repository_owner_id.")).ToBeVisibleAsync();
        await Expect(add.SubmitButton).ToBeDisabledAsync();

        await add.Name.FillAsync(name);
        await add.ClaimValue(1).FillAsync(RepositoryOwnerId);
        await Expect(add.SubmitButton).ToBeEnabledAsync();
        addedIssuers.Add((project, name));
        await add.SubmitAsync(name);
        await Expect(issuers.NoTrustedIssuers).ToHaveCountAsync(0);
        await Expect(issuers.IssuerCell(name, "Issuer")).ToHaveTextAsync(GitHubIssuer);
        await Expect(issuers.IssuerCell(name, "Key source")).ToHaveTextAsync("Discovery");
        await Expect(issuers.IssuerCell(name, "Max lifetime")).ToHaveTextAsync("1 h (default)");
        await Expect(issuers.IssuerCell(name, "Required claims")).ToHaveTextAsync($"repository_owner_id = {RepositoryOwnerId}");

        var edit = issuers.EditDialog(name);
        await issuers.EditButton(name).ClickAsync();
        await Expect(edit.Root).ToBeVisibleAsync();
        await Expect(edit.Template).ToHaveCountAsync(0);
        await Expect(edit.Name).ToBeDisabledAsync();
        await Expect(edit.Issuer).Not.ToBeEditableAsync();
        await Expect(edit.IssuerHelp).ToHaveTextAsync("The issuer cannot change: the rules were written for it. Add a new trusted issuer instead.");

        var description = PlatformData.Title();
        await edit.Description.FillAsync(description);

        // The same toast follows: wait for the first one to close.
        await Expect(App.Toast(saved)).ToBeHiddenAsync();
        await edit.SubmitAsync(name);
        await Expect(issuers.IssuerDescription(name)).ToHaveTextAsync(description);

        var delete = issuers.DeleteDialog(name);
        await issuers.DeleteButton(name).ClickAsync();
        await delete.ExpectOpenAsync();
        await Expect(delete.Root).ToHaveAccessibleDescriptionAsync("Rules that use it stop matching new exchanges within a minute; Storyteller tokens already issued stay valid until they expire. Its name can't be used again while rules of machines still name it.");
        await Expect(delete.Button("Delete")).ToBeDisabledAsync();

        await Expect(delete.Root.GetByRole(AriaRole.Textbox, new() { Name = $"Type {name} to confirm", Exact = true })).ToBeVisibleAsync();
        await delete.TypeConfirmationAsync(name);
        await delete.ConfirmAsync("Delete");
        await Expect(App.Toast($"Trusted issuer {name} deleted")).ToBeVisibleAsync();
        addedIssuers.Remove((project, name));
        await Expect(issuers.IssuerRow(name)).ToHaveCountAsync(0);
        await Expect(issuers.NoTrustedIssuers).ToBeVisibleAsync();
    }

    // ACC-FED-02: Issuer templates fill the dialog, and invalid or taken names and an http issuer are refused.
    // Vercel, Azure managed identity and Kubernetes fill the name, the issuer (with its placeholder to replace), the
    // lifetime and the claims; an invalid and a taken name are refused in the field; the API refuses an http issuer in
    // the dialog, and Cancel adds nothing.
    [Test]
    [TestCase(206)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task TrustedIssuers_TemplatesAndValidation_GuideInput()
    {
        var project = await CreateDisposableProjectAsync();
        var (taken, refused) = (PlatformData.TrustedIssuerName(), PlatformData.TrustedIssuerName());

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        var issuers = machines.TrustedIssuers;
        addedIssuers.Add((project, taken));
        await issuers.AddGitHubIssuerAsync(taken, RepositoryOwnerId);

        var dialog = await issuers.OpenAddDialogAsync();
        await dialog.PickTemplateAsync("Vercel");
        await Expect(dialog.Name).ToHaveValueAsync("vercel");
        await Expect(dialog.Issuer).ToHaveValueAsync("https://oidc.vercel.com/<team-slug>");
        await Expect(dialog.IssuerError).ToHaveTextAsync("Replace <team-slug> with the value of your platform.");
        await Expect(dialog.MaxTokenLifetime).ToHaveValueAsync(Lifetime7200());
        await Expect(dialog.ClaimName(1)).ToHaveValueAsync("owner_id");

        await dialog.PickTemplateAsync("Azure managed identity");
        await Expect(dialog.Name).ToHaveValueAsync("azure");
        await Expect(dialog.Issuer).ToHaveValueAsync("https://login.microsoftonline.com/<tenant-id>/v2.0");
        await Expect(dialog.IssuerError).ToHaveTextAsync("Replace <tenant-id> with the value of your platform.");
        await Expect(dialog.MaxTokenLifetime).ToHaveValueAsync(Lifetime86400());
        await Expect(dialog.ClaimName(1)).ToHaveCountAsync(0);
        await Expect(dialog.SubmitButton).ToBeDisabledAsync();

        await dialog.PickTemplateAsync("Kubernetes");
        await Expect(dialog.Name).ToHaveValueAsync("kubernetes");
        await Expect(dialog.Issuer).ToBeEmptyAsync();
        await Expect(dialog.Issuer).ToHaveAttributeAsync("placeholder", "https://westeurope.oic.prod-aks.azure.com/<tenant>/<id>/");
        await Expect(dialog.MaxTokenLifetime).ToBeEmptyAsync();
        await Expect(dialog.TemplateHelp("Kubernetes")).ToContainTextAsync("oidcIssuerProfile.issuerUrl");

        await dialog.Name.FillAsync("Not valid");
        await Expect(dialog.NameError).ToHaveTextAsync("Use 1 to 63 lower-case letters, digits or hyphens, starting and ending with a letter or a digit.");

        await dialog.Name.FillAsync(taken);
        await Expect(dialog.NameError).ToHaveTextAsync($"The trusted issuer {taken} exists already; edit it in the list.");

        // Custom keeps the typed name, so the new name follows it.
        await dialog.PickTemplateAsync("Custom");
        await dialog.Name.FillAsync(refused);
        await dialog.Issuer.FillAsync("http://idp.example.com");

        // A deployment that allows private networks (development only) would store it; the cleanup deletes it then.
        addedIssuers.Add((project, refused));
        await dialog.SubmitButton.ClickAsync();
        await Expect(dialog.Failure("The issuer http://idp.example.com cannot be discovered: it must use https.")).ToBeVisibleAsync();
        await Expect(dialog.Root).ToBeVisibleAsync();

        await dialog.CancelButton.ClickAsync();
        await Expect(dialog.Root).ToBeHiddenAsync();
        await Expect(issuers.IssuerRows).ToHaveCountAsync(1);
        await Expect(issuers.IssuerRow(taken)).ToBeVisibleAsync();
        await Expect(issuers.IssuerRow(refused)).ToHaveCountAsync(0);
        addedIssuers.Remove((project, refused));
    }

    // ACC-FED-03: From no trusted issuer to a Federated machine with its setup.
    // With Federated enabled and no trusted issuer, New machine's Federated kind sends an Administrator to the Trusted
    // issuers section; with a GitHub issuer added there, Rule 1 preselects it with the project's token endpoint, and the
    // created machine shows its setup (no secret) and lists as Federated without Reset.
    [Test]
    [TestCase(209)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Federation_NoIssuerToFederatedMachine_ShowsSetup()
    {
        var project = await CreateDisposableProjectAsync();
        var (name, subject) = (PlatformData.TrustedIssuerName(), PlatformData.GitHubSubject());
        await new ProjectSettingsPage(App, project).SaveMachinePolicyWithKindAsync("Federated");

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        var create = await machines.OpenCreateDialogAsync();
        await create.PickCredentialKindAsync("Federated");
        await Expect(create.NoTrustedIssuers).ToBeVisibleAsync();
        await Expect(create.NoTrustedIssuersHint).ToHaveTextAsync("A Federated machine exchanges tokens of a trusted issuer of the project. Add one first, for example for GitHub Actions or Kubernetes.");
        await Expect(create.CreateButton).ToBeDisabledAsync();

        await create.AddTrustedIssuerLink.PressAsync("Enter");
        await Expect(create.Root).ToBeHiddenAsync();
        await App.ExpectAtAsync(project.Machines);
        await Expect(Page).ToHaveURLAsync(TrustedIssuersHash());
        var issuers = machines.TrustedIssuers;

        // The section takes the focus once the dialog's leave transition ended.
        await Expect(issuers.Root).ToBeFocusedAsync();
        await Expect(issuers.Root).ToBeInViewportAsync();
        await Expect(issuers.NoTrustedIssuers).ToBeVisibleAsync();

        addedIssuers.Add((project, name));
        await issuers.AddGitHubIssuerAsync(name, RepositoryOwnerId);

        await machines.OpenCreateDialogAsync();
        await create.PickCredentialKindAsync("Federated");
        var rule = create.Rule(1);
        await Expect(rule.Root).ToBeVisibleAsync();
        await Expect(rule.TrustedIssuer).ToHaveTextAsync(name);
        await Expect(rule.Audience).ToHaveValueAsync(TokenEndpoint(project));
        await Expect(rule.TrustedIssuerHelp).ToHaveTextAsync($"Every token of this issuer must also carry repository_owner_id = {RepositoryOwnerId}.");
        await Expect(create.CreateButton).ToBeDisabledAsync();

        await rule.Subject.FillAsync(subject);
        var setup = await create.CreateFederatedAsync();
        var id = await setup.MachineIdAsync();
        createdMachines.Add((project, id));
        await Expect(setup.Root).ToHaveAccessibleDescriptionAsync(FederatedSetupDialog.DescriptionText);
        await Expect(setup.Snippet("Token exchange (curl)")).ToContainTextAsync($"-d client_id={id}");
        await Expect(setup.Snippet("GitHub Actions step")).ToContainTextAsync("id-token: write");
        await Expect(Page.GetByText("Shown only once", new() { Exact = true })).ToHaveCountAsync(0);

        await setup.DoneAsync();
        await Expect(machines.MachineCell(id, "Credentials")).ToHaveTextAsync("Federated");
        await Expect(machines.DeleteButton(id)).ToBeVisibleAsync();
        await Expect(machines.ResetButton(id)).ToHaveCountAsync(0);
    }

    // ACC-FED-04: Federation rules are set up and edited on the machine detail.
    // A Federated machine's detail lists its rule with Setup and Edit rules; Setup shows the accepted token and closes
    // with Escape; Edit rules saves a changed subject; a second rule with another service's audience is refused in the
    // dialog, and Cancel keeps the saved rule alone.
    [Test]
    [TestCase(216)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Federation_EditRules_SavesAndValidates()
    {
        var project = await CreateDisposableProjectAsync();
        var (name, subject) = (PlatformData.TrustedIssuerName(), PlatformData.GitHubSubject());
        var staging = subject.Replace(":environment:production", ":environment:staging", StringComparison.Ordinal);
        await new ProjectSettingsPage(App, project).SaveMachinePolicyWithKindAsync("Federated");

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        addedIssuers.Add((project, name));
        await machines.TrustedIssuers.AddGitHubIssuerAsync(name, RepositoryOwnerId);
        var id = await machines.CreateDialog.CreateFederatedMachineAsync(machines, name, subject, created => createdMachines.Add((project, created)));

        var detail = await machines.OpenMachineAsync(id);
        await Expect(detail.FederationRules).ToBeVisibleAsync();
        await Expect(detail.RuleItems).ToHaveCountAsync(1);
        await Expect(detail.RuleItem(1)).ToBeVisibleAsync();
        await Expect(detail.RuleIssuer(1)).ToHaveTextAsync(name);
        await Expect(detail.RuleValue(1, "Subject")).ToHaveTextAsync(subject);
        await Expect(detail.SetupButton).ToBeVisibleAsync();
        await Expect(detail.EditRulesButton).ToBeVisibleAsync();

        // The audience is the dd's own text; a flag of another audience would follow it.
        var audience = await detail.RuleValue(1, "Audience").EvaluateAsync<string>("dd => dd.firstChild.textContent.trim()");
        await detail.SetupButton.ClickAsync();
        var setup = detail.SetupDialog;
        await Expect(setup.Root).ToBeVisibleAsync();
        await Expect(setup.MachineId).ToHaveTextAsync(id);
        await Expect(setup.TokenEndpoint).ToBeVisibleAsync();
        await Expect(setup.AcceptedTokens).ToHaveTextAsync([AcceptedToken(1, name, audience, subject)]);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(setup.Root).ToBeHiddenAsync();
        await Expect(detail.Root).ToBeVisibleAsync();

        var rules = detail.RulesDialog;
        await detail.EditRulesButton.ClickAsync();
        await rules.WaitForOpenAsync();
        await Expect(rules.Root).ToHaveAccessibleDescriptionAsync(rules.DescriptionText);
        await rules.Rule(1).Subject.FillAsync(staging);
        await rules.SaveAsync();
        await Expect(detail.RuleValue(1, "Subject")).ToHaveTextAsync(staging);

        await detail.EditRulesButton.ClickAsync();
        await rules.WaitForOpenAsync();
        await rules.AddRuleButton.ClickAsync();
        var second = rules.Rule(2);
        await Expect(second.Root).ToBeVisibleAsync();
        await Expect(second.Audience).ToHaveValueAsync(audience);
        await Expect(second.TrustedIssuer).ToHaveTextAsync(name);
        await Expect(second.RemoveButton).ToBeVisibleAsync();

        await second.Audience.FillAsync("sts.amazonaws.com");
        await second.Subject.FillAsync(PlatformData.GitHubSubject());
        await rules.SaveRulesButton.ClickAsync();
        await Expect(rules.Failure("Rule 2: the audience sts.amazonaws.com belongs to another service.")).ToBeVisibleAsync();
        await Expect(rules.Root).ToBeVisibleAsync();

        await rules.CancelAsync();
        await Expect(detail.RuleItems).ToHaveCountAsync(1);
        await Expect(detail.RuleItem(2)).ToHaveCountAsync(0);
        await Expect(detail.RuleValue(1, "Subject")).ToHaveTextAsync(staging);
    }

    // ACC-FED-05: The JWKS URI and Pasted JWKS key sources need their keys, and the Key source column shows them.
    // A Custom issuer starts with Discovery; JWKS URI asks for its URI (the API refuses http), Pasted JWKS for the key
    // set, which saves the issuer with "Pasted JWKS"; its edit shows the pasted set and switches it to a JWKS URI.
    [Test]
    [TestCase(217)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task TrustedIssuers_KeySources_RequireJwks()
    {
        const string issuerUrl = "https://idp.example.com";
        const string jwksUri = "https://keys.example.com/openid/v1/jwks";

        var project = await CreateDisposableProjectAsync();
        var (name, jwks) = (PlatformData.TrustedIssuerName(), PlatformData.PublicRsaJwks());
        var saved = $"Trusted issuer {name} saved";

        var machines = new MachinesPage(App, project);
        await machines.OpenAsync();
        var issuers = machines.TrustedIssuers;
        var dialog = await issuers.OpenAddDialogAsync();
        await Expect(dialog.KeySource).ToHaveTextAsync("Discovery");
        await Expect(dialog.KeySourceHelp).ToHaveTextAsync("The issuer’s discovery document (/.well-known/openid-configuration) names its keys.");

        // The name of the GitHub template follows the template, so Custom empties it.
        await dialog.PickTemplateAsync("Custom");
        await Expect(dialog.Name).ToBeEmptyAsync();
        await dialog.Name.FillAsync(name);
        await dialog.Issuer.FillAsync(issuerUrl);
        await Expect(dialog.TemplateHelp("Custom")).ToHaveTextAsync("Any OIDC issuer: the exact iss of its tokens (case and a trailing / matter), and its keys from the discovery document, a JWKS URI or a pasted JWKS.");
        await Expect(dialog.SubmitButton).ToBeEnabledAsync();

        await dialog.PickKeySourceAsync("JWKS URI");
        await Expect(dialog.KeySourceHelp).ToHaveTextAsync("The keys are public at a JWKS URI, the discovery document is not (a cluster with --service-account-jwks-uri).");
        await Expect(dialog.JwksUri).ToHaveAttributeAsync("placeholder", jwksUri);
        await Expect(dialog.SubmitButton).ToBeDisabledAsync();

        await dialog.JwksUri.FillAsync("http://keys.example.com/jwks");
        addedIssuers.Add((project, name));
        await dialog.SubmitButton.ClickAsync();
        await Expect(dialog.Failure("The JWKS URI http://keys.example.com/jwks is not valid: it must use https.")).ToBeVisibleAsync();
        await Expect(dialog.Root).ToBeVisibleAsync();

        await dialog.PickKeySourceAsync("Pasted JWKS");
        await Expect(dialog.KeySourceHelp).ToHaveTextAsync("The keys are pasted here and never fetched (a self-managed cluster). Paste them again after a key rotation.");
        await Expect(dialog.JwksUri).ToHaveCountAsync(0);
        await Expect(dialog.Jwks).ToBeVisibleAsync();
        await Expect(dialog.JwksHelp).ToHaveTextAsync("The JSON Web Key Set with 1 to 10 public RSA or EC keys, each with a kid; for example the output of kubectl get --raw /openid/v1/jwks.");
        await Expect(dialog.SubmitButton).ToBeDisabledAsync();

        await dialog.Jwks.FillAsync(jwks);
        await dialog.SubmitAsync(name);
        await Expect(issuers.IssuerCell(name, "Issuer")).ToHaveTextAsync(issuerUrl);
        await Expect(issuers.IssuerCell(name, "Key source")).ToHaveTextAsync("Pasted JWKS");
        await Expect(issuers.IssuerCell(name, "Max lifetime")).ToHaveTextAsync("1 h (default)");
        await Expect(issuers.IssuerCell(name, "Required claims")).ToHaveTextAsync("–");

        var edit = issuers.EditDialog(name);
        await issuers.EditButton(name).ClickAsync();
        await Expect(edit.Root).ToBeVisibleAsync();
        await Expect(edit.KeySource).ToHaveTextAsync("Pasted JWKS");
        await Expect(edit.Jwks).ToHaveValueAsync(jwks);

        await edit.PickKeySourceAsync("JWKS URI");
        await Expect(edit.JwksUri).ToBeEmptyAsync();
        await Expect(edit.SubmitButton).ToBeDisabledAsync();
        await edit.JwksUri.FillAsync(jwksUri);

        // The same toast follows: wait for the first one to close.
        await Expect(App.Toast(saved)).ToBeHiddenAsync();
        await edit.SubmitAsync(name);
        await Expect(issuers.IssuerCell(name, "Key source")).ToHaveTextAsync("JWKS URI");
    }

    // Deletes the machines the test created, then its trusted issuers (rules may name them), project by project.
    protected override async Task CleanUpAsync()
    {
        foreach (var project in createdMachines.Select(machine => machine.Routes).Concat(addedIssuers.Select(issuer => issuer.Routes)).Distinct())
        {
            var machines = new MachinesPage(App, project);
            await machines.OpenAsync();
            foreach (var (_, id) in createdMachines.Where(machine => machine.Routes == project))
            {
                await machines.DeleteMachineIfListedAsync(id);
            }

            foreach (var (_, name) in addedIssuers.Where(issuer => issuer.Routes == project))
            {
                await machines.TrustedIssuers.DeleteIssuerIfListedAsync(name);
            }
        }
    }

    // The project's token endpoint, which New machine presets as the audience: <API base>/v1/<org>/<project>/access/token.
    private static Regex TokenEndpoint(AppRoutes project)
    {
        return new Regex($@"\/v1\/{Regex.Escape(project.Organization)}\/{Regex.Escape(project.Project)}\/access\/token$");
    }

    // An item of the setup's "Accepted tokens": "Rule <n>: <issuer>, audience <audience>, subject <subject>". A deployed
    // target behind a proxy may name its token endpoint differently from the address the UI calls the API at; the
    // item then flags the audience, which depends on the deployment, not on the rule.
    private static Regex AcceptedToken(int number, string issuer, string audience, string subject)
    {
        static string Escape(string text) => Regex.Escape(text).Replace("/", @"\/", StringComparison.Ordinal);

        return new Regex(
            $@"^\s*Rule {number}:\s*{Escape(issuer)}, audience {Escape(audience)}, subject {Escape(subject)}" +
            @"(\s*\(not the token endpoint: request the platform token with exactly this audience\))?\s*$");
    }

    [GeneratedRegex("#trusted-issuers$")]
    private static partial Regex TrustedIssuersHash();

    // The lifetime with or without the grouping comma of the browser's locale.
    [GeneratedRegex("^7,?200$")]
    private static partial Regex Lifetime7200();

    [GeneratedRegex("^86,?400$")]
    private static partial Regex Lifetime86400();
}
