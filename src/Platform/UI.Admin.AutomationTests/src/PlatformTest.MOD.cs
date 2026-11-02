using _42.Platform.UI.Admin.AutomationTests.Support;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests;

// The Modeling section's helper of the tests: a download whose confirmation is checked before its bytes are read.
public abstract partial class PlatformTest
{
    // Runs the action and returns the file it downloads, like DownloadAsync(action, timeoutMs), but runs started with the
    // suggested file name as soon as the download begins, before its bytes are read: a toast that confirms the download
    // lasts about 5 seconds, and reading a large file over a remote browser's connection can take longer.
    protected async Task<DownloadedFile> DownloadAsync(Func<Task> action, Func<string, Task> started, int timeoutMs = 60_000)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(started);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);

        var download = await Page.RunAndWaitForDownloadAsync(action, new() { Timeout = timeoutMs });
        await started(download.SuggestedFilename);
        await using var stream = await download.CreateReadStreamAsync()
            ?? throw new InvalidOperationException($"The download of {download.SuggestedFilename} has no content: {await download.FailureAsync()}");
        using var content = new MemoryStream();
        await stream.CopyToAsync(content);
        return new DownloadedFile(download.SuggestedFilename, content.ToArray());
    }
}
