using System.Globalization;
using System.Text.RegularExpressions;

namespace _42.Testing.UI.Automation.Reporting.Testiny;

// The Reporting:Testiny section. ApiKey is a secret: user secrets or an environment variable, never a committed file.
public sealed partial class TestinyOptions
{
    // The section of the options: Reporting:<System>, as the runtime binds a reporter's settings.
    public const string SectionName = "Reporting:" + TestinyTestRunReporter.SystemName;

    public const string DefaultBaseUrl = "https://app.testiny.io/api/v1/";

    // NOT VERIFIED: Testiny's API documentation does not describe the URL scheme of its web app, and no account was at
    // hand to read one ("Copy link to test run" in a run's menu shows it). Set RunUrlTemplate to that form when it
    // differs; {AppUrl} is BaseUrl without its /api/... path.
    public const string DefaultRunUrlTemplate = "{AppUrl}/{ProjectId}/testruns/{RunId}";

    public const long DefaultMaxAttachmentBytes = 25L * 1024 * 1024;

    // 256 KiB/s (about 2 Mbit/s): a file of DefaultMaxAttachmentBytes may take 100 s.
    public const long DefaultMinUploadBytesPerSecond = 256L * 1024;

    private static readonly string[] RunUrlPlaceholders = ["AppUrl", "ProjectId", "RunId"];

    // The API root, e.g. https://app.testiny.io/api/v1/.
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    // Sent as the X-Api-Key header; best restricted to the project.
    public string ApiKey { get; set; } = string.Empty;

    // The numeric id of the Testiny project the runs and test cases belong to.
    public long ProjectId { get; set; }

    // A test plan the run is added to; none when null.
    public long? TestPlanId { get; set; }

    public TestinyMode Mode { get; set; } = TestinyMode.TestRun;

    // Puts every linked case of the planned tests into the run as NOTRUN at the start, so the run shows what is pending.
    public bool PlanCasesAsNotRun { get; set; } = true;

    public bool CloseRunOnComplete { get; set; } = true;

    // Uploads the artifacts (failure.png, console.log, video.webm; trace.zip only with AttachTraces) of these results to
    // each linked case.
    public TestinyContentPolicy Attachments { get; set; } = TestinyContentPolicy.Failures;

    // Uploads the Playwright traces as well. Off by default: a trace holds the cookies and storage of the signed-in
    // pool user (a live session until it expires) and every request of the test, so turning it on shares that session
    // with Testiny and everyone who can read the project.
    public bool AttachTraces { get; set; }

    // Larger files are not uploaded; the comment lists them instead.
    public long MaxAttachmentBytes { get; set; } = DefaultMaxAttachmentBytes;

    // Writes the markdown comment (duration, environment, stack trace) of these results to each linked case.
    public TestinyContentPolicy Comments { get; set; } = TestinyContentPolicy.Failures;

    // The result status of a skipped and of a blocked test; a project may use BLOCKED for skips, or a custom status.
    public string ResultForSkipped { get; set; } = TestinyResultStatus.Skipped;

    public string ResultForBlocked { get; set; } = TestinyResultStatus.Blocked;

    // Bounds every request; a request that takes longer counts as a transient failure and is tried again.
    public int RequestTimeoutSeconds { get; set; } = 30;

    // The slowest upload expected: an attachment may take its size divided by this, when that is longer than
    // RequestTimeoutSeconds.
    public long MinUploadBytesPerSecond { get; set; } = DefaultMinUploadBytesPerSecond;

    // The browser link of a run (ITestRunReporter.RunUrl) with {AppUrl}, {ProjectId} and {RunId}; empty for none.
    public string? RunUrlTemplate { get; set; } = DefaultRunUrlTemplate;

    // The scheme and host of BaseUrl, without the API path: https://app.testiny.io.
    public string AppUrl
    {
        get
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var path = uri.AbsolutePath;
            var api = path.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
            var prefix = api >= 0 ? path[..api] : path.TrimEnd('/');
            return $"{uri.GetLeftPart(UriPartial.Authority)}{prefix}";
        }
    }

    // The API root with a trailing slash, for relative request paths.
    public Uri GetBaseUri()
    {
        var url = BaseUrl.Trim();
        return new Uri(url.EndsWith('/') ? url : url + "/", UriKind.Absolute);
    }

    // Null without a template.
    public Uri? FormatRunUrl(long runId)
    {
        if (string.IsNullOrWhiteSpace(RunUrlTemplate))
        {
            return null;
        }

        var url = PlaceholderPattern().Replace(RunUrlTemplate.Trim(), match => match.Groups["name"].Value.ToLowerInvariant() switch
        {
            "appurl" => AppUrl,
            "projectid" => ProjectId.ToString(CultureInfo.InvariantCulture),
            "runid" => runId.ToString(CultureInfo.InvariantCulture),
            _ => match.Value,
        });

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    // The messages start with the key below section (the runtime puts "Reporting:Testiny: " in front) and name the
    // whole key where it is to be set; never a secret value.
    public IReadOnlyList<string> Validate(string section = SectionName)
    {
        var errors = new List<string>();

        // The API key goes in a header of every request, so plain http is for a local fake or proxy only.
        if (!Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback)))
        {
            errors.Add($"BaseUrl must be an absolute https URL such as {DefaultBaseUrl} (http only for a loopback host such as localhost); it is '{BaseUrl}'.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"ApiKey is empty; set {section}:ApiKey in the user secrets of the test project or in an environment variable (UITESTS_{section.Replace(":", "__", StringComparison.Ordinal)}__ApiKey).");
        }

        if (ProjectId < 1)
        {
            errors.Add($"ProjectId is required: the numeric id of the Testiny project ({section}:ProjectId).");
        }

        if (TestPlanId is < 1)
        {
            errors.Add("TestPlanId must be a positive test plan id, or null for no plan.");
        }

        if (Mode == TestinyMode.Automation)
        {
            errors.Add("Mode Automation (Testiny's automation API) is not implemented yet; use TestRun.");
        }
        else if (!Enum.IsDefined(Mode))
        {
            errors.Add("Mode must be TestRun.");
        }

        if (MaxAttachmentBytes < 1)
        {
            errors.Add("MaxAttachmentBytes must be positive.");
        }

        if (RequestTimeoutSeconds < 1)
        {
            errors.Add("RequestTimeoutSeconds must be at least 1.");
        }

        if (MinUploadBytesPerSecond < 1)
        {
            errors.Add("MinUploadBytesPerSecond must be positive.");
        }

        RequireStatus(ResultForSkipped, "ResultForSkipped", errors);
        RequireStatus(ResultForBlocked, "ResultForBlocked", errors);

        if (!string.IsNullOrWhiteSpace(RunUrlTemplate))
        {
            foreach (Match match in PlaceholderPattern().Matches(RunUrlTemplate))
            {
                var name = match.Groups["name"].Value;
                if (!RunUrlPlaceholders.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"RunUrlTemplate has the unknown placeholder {{{name}}}; use {string.Join(", ", RunUrlPlaceholders.Select(placeholder => $"{{{placeholder}}}"))}.");
                }
            }
        }

        return errors;
    }

    private static void RequireStatus(string? status, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(status) || !StatusPattern().IsMatch(status))
        {
            errors.Add($"{key} must be a result status such as {TestinyResultStatus.Skipped} or {TestinyResultStatus.Blocked} (upper-case letters, digits and underscores); it is '{status}'.");
        }
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex StatusPattern();
}
