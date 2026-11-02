using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Settings section's member of an annotation's detail: the "Time zone" of its form (help "Dates are entered and
// shown in this zone."), the zone the annotation was saved with; without one it shows the placeholder "Browser time
// zone (<zone>)".
public sealed partial class AnnotationDetail
{
    public TimeZoneSelect FormTimeZone => new(app, Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Time zone", Exact = true }));
}
