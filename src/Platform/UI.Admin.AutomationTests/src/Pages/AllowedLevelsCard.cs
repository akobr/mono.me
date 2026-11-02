using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The card "Allowed levels" of the Secrets section (settings#secrets; components/secrets/SecretsPolicyCard.vue): a row
// per level (Public, Standard, HSM, BYOK, HYOK) with its switch "Allow <level>", its badge, its cost, its description
// and, for HSM, BYOK and HYOK, what it needs ("Needs: …"); then "Default secret level" (only the allowed secret levels),
// the mTLS rule of the field "Machines" and, for Administrators, "Save secrets policy" (disabled until something
// changed). Turning the mTLS rule off asks "Deliver secrets to machines without mTLS?" on Save. A refused save shows
// an alert in the card and no toast: a level that keys still carry is a warning "<Level> is still in use" with a link
// "<count> in <view>" per view, anything else an error alert with the API's message (and its hint). A save in another
// card of the section refetches the policy, which resets the switches that were not saved.
public sealed partial class AllowedLevelsCard(App app, AppRoutes routes)
{
    // The confirmation of turning the mTLS rule off, and its description.
    public const string MtlsDialogTitle = "Deliver secrets to machines without mTLS?";

    public const string MtlsDialogDescription = "Machines with an API key or a bearer token alone will receive secret values when they hold the Secrets.Read scope.";

    // The alert of a save the API refused because the policy changed since the page read it (409 PolicyVersionMismatch).
    public const string PolicyChangedMeanwhile = "The policy changed meanwhile; reload the page and try again.";

    private readonly ProjectSettingsPage settings = new(app, routes);

    public ILocator Root => settings.AllowedLevelsCard;

    // The combobox of the level new secrets get; its options are the allowed secret levels by their labels.
    public ILocator DefaultSecretLevel => settings.DefaultSecretLevel;

    // The switch of the mTLS rule. Its accessible name is its field's label "Machines", which wins over its own label
    // "Require mTLS for secret delivery", so it is the switch of the Nuxt UI switch around that label.
    public ILocator MtlsSwitch => Root
        .GetByText("Require mTLS for secret delivery", new() { Exact = true })
        .Locator("xpath=ancestor::*[@data-slot='root'][1]")
        .GetByRole(AriaRole.Switch);

    public ILocator SaveButton => settings.SaveSecretsPolicyButton;

    public ConfirmDialog MtlsDialog => new(app, MtlsDialogTitle);

    // The text of the card's error alert (a refused save other than a level in use): the API's message and its hint.
    // The alert has a description only, unlike the warning of a level in use, which has a title too.
    public ILocator ErrorText => Root.Locator("[data-slot='wrapper']:not(:has([data-slot='title'])) > [data-slot='description']");

    // The text after the title of the warning of a level in use, before its links.
    public ILocator InUseIntro => Root.GetByText("Move these keys to another level first:", new() { Exact = true });

    // The switch "Allow <level>", by the level's label (Public, Standard, HSM, BYOK, HYOK) or its API name.
    public ILocator Allow(string level)
    {
        return settings.AllowLevel(level);
    }

    // The row of a level: its switch, badge, cost, description and requirement.
    public ILocator Level(string level)
    {
        return Allow(level).Locator("xpath=ancestor::li[1]");
    }

    // What the level needs, "Needs: <requirement>" (HSM, BYOK and HYOK; none for Public and Standard).
    public ILocator Requirement(string level)
    {
        return Level(level).GetByText(Needs());
    }

    // An option of the open "Default secret level" by the level's label ("Standard", "HSM", "BYOK", "HYOK").
    public ILocator DefaultSecretLevelOption(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = label, Exact = true });
    }

    // The title of the warning of a level in use, by the level's API name ("Standard", "Hsm", "Byok", "Hyok").
    public ILocator InUseTitle(string level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        return Root.GetByText($"{level} is still in use", new() { Exact = true });
    }

    // A link of the warning of a level in use, "<count> in <view>", to the view's values store filtered by the level.
    public ILocator InUseLink(int count, string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        return Root.GetByRole(AriaRole.Link, new() { Name = $"{count} in {view}", Exact = true });
    }

    // Picks the default secret level by its label: expects it among the offered (allowed) levels, picks it and waits
    // until the select shows it; save it with SaveAsync.
    public async Task SelectDefaultSecretLevelAsync(string label)
    {
        await DefaultSecretLevel.ClickAsync();
        var option = DefaultSecretLevelOption(label);
        await Expect(option).ToBeVisibleAsync();
        await option.ClickAsync();
        await Expect(DefaultSecretLevel).ToHaveTextAsync(label);
    }

    // Saves and waits for the toast "Secrets policy saved".
    public async Task SaveAsync()
    {
        await settings.SaveSecretsPolicyAsync();
    }

    // Allows a level that the deployment may not offer (HSM needs an HSM provider): turns "Allow <level>" on, saves,
    // and returns once the toast "Secrets policy saved" showed. When the card shows the API's ProtectionNotConfigured
    // instead ("The protection level '<Level>' is not configured in this deployment."), the test is skipped with that
    // text; any other alert fails it.
    public async Task AllowLevelOrSkipAsync(string level)
    {
        await settings.AllowLevelAsync(level);
        var saved = app.Toast("Secrets policy saved");
        await SaveButton.ClickAsync();
        await Expect(saved.Or(ErrorText)).ToBeVisibleAsync();
        if (!await ErrorText.IsVisibleAsync())
        {
            return;
        }

        var refusal = (await ErrorText.InnerTextAsync()).Trim();
        if (NotConfigured().IsMatch(refusal))
        {
            Skip.Test(refusal);
        }

        throw new InvalidOperationException($"Allowing {level} in the project {routes.Organization}/{routes.Project} was refused: {refusal}");
    }

    [GeneratedRegex("^Needs: ")]
    private static partial Regex Needs();

    [GeneratedRegex("^The protection level '[A-Za-z]+' is not configured in this deployment\\.")]
    private static partial Regex NotConfigured();
}
