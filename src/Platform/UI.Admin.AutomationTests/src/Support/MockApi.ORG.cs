namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The Organizations and Projects section's calls of the mock API: the join of the deployment's exhibition behind the
// page's back, as from another tab. The page shows the membership after its next load (App.GotoAsync keeps the tab's
// data).
public sealed partial class MockApi
{
    // Joins the exhibition as the signed-in user (POST v1/access/exhibition/join): a Reader of its project, without a
    // role in its organization. Returns the answer: 200 with the account, or 404 while the mock has no exhibition
    // (SetExhibitionAsync(null)).
    public Task<MockApiResponse> JoinExhibitionAsync()
    {
        return FetchAsync("POST", "v1/access/exhibition/join");
    }
}
