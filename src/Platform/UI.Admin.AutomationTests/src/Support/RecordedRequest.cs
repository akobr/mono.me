namespace _42.Platform.UI.Admin.AutomationTests.Support;

// A fetch of the page that PlatformTest.InstallRequestRecorderAsync recorded: the method (upper case), the absolute URL
// and the body as text (null without one; "[Blob]", "[FormData]", … for a body that is not text).
public sealed record RecordedRequest(string Method, string Url, string? Body)
{
    // The path of the URL, without its query (the URL itself if it is not absolute).
    public string Path => Uri.TryCreate(Url, UriKind.Absolute, out var url) ? url.AbsolutePath : Url;
}
