using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Newtonsoft.Json.Linq;
using SdkAppearance = _42.Platform.Storyteller.Sdk.ProjectTypeAppearance;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.UnitTests.Commands.Projects;

// The doubles of the project settings commands: the console and context of the annotation commands (org1.proj1), the
// access API with its settings twins, a file system and the cache of the settings.
internal sealed class ProjectSettingsFixture
{
    public const string Organization = AnnotationCommandFixture.Organization;
    public const string Project = AnnotationCommandFixture.Project;
    public const string ProjectKey = $"{Organization}.{Project}";

    public static readonly DateTimeOffset ModifiedAt = new(2026, 11, 15, 10, 22, 0, TimeSpan.Zero);

    private readonly AnnotationCommandFixture _annotations = new();

    public RecordingConsole Console => _annotations.Console;

    public Mock<ICommandContext> Context => _annotations.Context;

    public Mock<IAccessApiClient> Access { get; } = new(MockBehavior.Strict);

    public Mock<IProjectSettingsCache> Cache { get; } = new();

    public MockFileSystem FileSystem { get; } = new();

    public IReadOnlyList<string> Lines => Console.Lines;

    // A project that changed sbt (a name and a pink building) and unt (a color), its time zone and its date format.
    public static SdkSettings Changed(long version = 3)
    {
        return new SdkSettings
        {
            Types = new Dictionary<string, SdkAppearance>
            {
                ["unt"] = new() { Color = "teal" },
                ["sbt"] = new() { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" },
            },
            DefaultTimeZone = "Europe/Prague",
            DateFormat = "dd.mm.yyyy",
            Version = version,
            Modified = new AuditStamp
            {
                At = ModifiedAt,
                By = new AuditActor { Kind = AuditActorKind.Account, Id = "user_1", Name = "Ales Kobr" },
            },
        };
    }

    // A project that never set its settings.
    public static SdkSettings Defaults(long version = 0)
    {
        return new SdkSettings { Types = new Dictionary<string, SdkAppearance>(), Version = version };
    }

    public static string TagOf(long version)
    {
        return $"\"{version}\"";
    }

    public static IReadOnlyDictionary<string, IEnumerable<string>> Headers(long version)
    {
        return new Dictionary<string, IEnumerable<string>> { ["ETag"] = [TagOf(version)] };
    }

    public static VersionedResponse<SdkSettings> Versioned(SdkSettings settings)
    {
        return new VersionedResponse<SdkSettings>(200, settings, Headers(settings.Version));
    }

    // The answer of the API with Details, as the SDK reads it (Newtonsoft turns an object member into a JToken).
    public static ApiException Error(int statusCode, string errorCode, string message, JToken? details = null)
    {
        return new ApiException<ErrorResponse>(
            $"HTTP {statusCode}",
            statusCode,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new ErrorResponse { ErrorCode = errorCode, Message = message, Details = details },
            null);
    }

    public static ApiException Forbidden()
    {
        return Error(403, "AccessDenied", "Changing the project settings needs a user with the Administrator role.");
    }

    public static ApiException PreconditionFailed(long version)
    {
        return Error(412, "SettingsPreconditionFailed", "The project settings are at another version.", new JObject { ["Version"] = version });
    }

    public static ApiException InvalidSettings(params (string Path, string Rule, string Message)[] problems)
    {
        var details = new JArray();

        foreach (var (path, rule, message) in problems)
        {
            details.Add(new JObject { ["Path"] = path, ["Rule"] = rule, ["Message"] = message });
        }

        return Error(400, "InvalidSettings", $"The settings break {problems.Length} rules.", details);
    }

    public void SetupRead(SdkSettings settings)
    {
        Access
            .Setup(api => api.GetProjectSettingsConditionalAsync(Organization, Project, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConditionalResponse<SdkSettings>(200, settings, Headers(settings.Version)));
    }

    public void SetupRead(Exception exception)
    {
        Access
            .Setup(api => api.GetProjectSettingsConditionalAsync(Organization, Project, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    // The patch the command sends, and the answer.
    public List<(JToken Patch, string? IfMatch)> SetupPatch(SdkSettings answer)
    {
        var sent = new List<(JToken, string?)>();
        Access
            .Setup(api => api.PatchProjectSettingsVersionedAsync(Organization, Project, It.IsAny<JToken>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, JToken, string?, CancellationToken>((_, _, patch, ifMatch, _) => sent.Add((patch.DeepClone(), ifMatch)))
            .ReturnsAsync(Versioned(answer));
        return sent;
    }

    public void SetupPatch(Exception exception)
    {
        Access
            .Setup(api => api.PatchProjectSettingsVersionedAsync(Organization, Project, It.IsAny<JToken>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    // The body the command sends, and the answer.
    public List<(SdkSettings Body, string? IfMatch)> SetupPut(SdkSettings answer)
    {
        var sent = new List<(SdkSettings, string?)>();
        Access
            .Setup(api => api.SetProjectSettingsVersionedAsync(Organization, Project, It.IsAny<SdkSettings>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, SdkSettings, string?, CancellationToken>((_, _, body, ifMatch, _) => sent.Add((body, ifMatch)))
            .ReturnsAsync(Versioned(answer));
        return sent;
    }

    public void SetupPut(Exception exception)
    {
        Access
            .Setup(api => api.SetProjectSettingsVersionedAsync(Organization, Project, It.IsAny<SdkSettings>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    public void VerifyRemembered(long version)
    {
        Cache.Verify(
            cache => cache.RememberAsync(Organization, Project, It.Is<SdkSettings>(settings => settings.Version == version), TagOf(version), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void VerifyNothingRemembered()
    {
        Cache.VerifyNoOtherCalls();
    }
}
