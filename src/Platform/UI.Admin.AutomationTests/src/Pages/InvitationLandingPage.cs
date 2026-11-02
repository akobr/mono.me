using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /invitations/accept?invitation_token=…&id=… (public, bare layout), the landing of an invitation e-mail. A signed-in
// visitor goes on to /invitations at once; a signed-out one reads "You were invited to 2S Platform" and continues to
// the sign-in (AuthKit with the invitation token; the mock signs in through /callback), which returns to /invitations.
public sealed partial class InvitationLandingPage(App app, AppRoutes routes)
{
    public const string IntroText = "Sign in, or create your account, with the email address the invitation was sent to.";

    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "You were invited to 2S Platform", Exact = true, Level = 1 });

    public ILocator Intro => app.Page.GetByText(IntroText, new() { Exact = true });

    public ILocator ContinueButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true });

    // Opens the landing of an invitation, signed out (signed in, the page goes on to /invitations).
    public async Task OpenAsync(string? id, string? token)
    {
        await app.GotoAsync(routes.InvitationAccept(id, token));
        await Expect(Heading).ToBeVisibleAsync();
    }
}
