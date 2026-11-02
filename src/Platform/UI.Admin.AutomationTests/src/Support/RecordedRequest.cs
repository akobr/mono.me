namespace _42.Platform.UI.Admin.AutomationTests.Support;

// A fetch of the page that PlatformTest.InstallRequestRecorderAsync recorded: the method (upper case), the absolute URL
// and the body as text (null without one; "[Blob]", "[FormData]", … for a body that is not text).
public sealed record RecordedRequest(string Method, string Url, string? Body);
