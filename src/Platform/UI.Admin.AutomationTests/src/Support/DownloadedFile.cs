using System.Text;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// A file the page downloaded (PlatformTest.DownloadAsync): the name the app suggested and the bytes, read over the
// browser's connection, so it works on BrowserStack too. It is not kept as an artifact: a downloaded certificate or a
// secret export must not reach the reports.
public sealed record DownloadedFile(string SuggestedFilename, byte[] Content)
{
    // The content as UTF-8 text (a JSON export, a PEM certificate).
    public string Text => Encoding.UTF8.GetString(Content);
}
