using System.Text;
using _42.Testing.UI.Automation.Reporting.Testiny.UnitTests.Fakes;
using Microsoft.Extensions.Configuration;

namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests;

public sealed class TestinyReporterProviderTests
{
    [Fact]
    public void System_IsTestiny()
    {
        TestinyReporterProvider.Instance.System.ShouldBe("Testiny");
        TestinyTestRunReporter.SystemName.ShouldBe("Testiny");
    }

    [Fact]
    public async Task Create_BindsTheSection()
    {
        var configuration = Configuration("""
            {
              "Reporting": {
                "Testiny": {
                  "BaseUrl": "https://testiny.example.test/api/v1/",
                  "ApiKey": "secret-key",
                  "ProjectId": 42,
                  "TestPlanId": 5,
                  "PlanCasesAsNotRun": false,
                  "CloseRunOnComplete": false,
                  "Attachments": "All",
                  "MaxAttachmentBytes": 1024,
                  "Comments": "None",
                  "ResultForSkipped": "BLOCKED",
                  "ResultForBlocked": "RETEST",
                  "RequestTimeoutSeconds": 5,
                  "RunUrlTemplate": "{AppUrl}/runs/{RunId}?project={ProjectId}"
                }
              }
            }
            """);

        var options = TestinyReporterProvider.BindOptions(configuration.GetSection(TestinyOptions.SectionName));
        await using var reporter = TestinyReporterProvider.Instance.Create(new Services(), configuration.GetSection(TestinyOptions.SectionName), TestData.Run());

        reporter.ShouldBeOfType<TestinyTestRunReporter>().System.ShouldBe("Testiny");
        options.BaseUrl.ShouldBe("https://testiny.example.test/api/v1/");
        options.ApiKey.ShouldBe("secret-key");
        options.ProjectId.ShouldBe(42);
        options.TestPlanId.ShouldBe(5);
        options.PlanCasesAsNotRun.ShouldBeFalse();
        options.CloseRunOnComplete.ShouldBeFalse();
        options.Attachments.ShouldBe(TestinyContentPolicy.All);
        options.MaxAttachmentBytes.ShouldBe(1024);
        options.Comments.ShouldBe(TestinyContentPolicy.None);
        options.ResultForSkipped.ShouldBe("BLOCKED");
        options.ResultForBlocked.ShouldBe("RETEST");
        options.RequestTimeoutSeconds.ShouldBe(5);
        options.FormatRunUrl(7).ShouldBe(new Uri("https://testiny.example.test/runs/7?project=42"));
    }

    [Fact]
    public void Create_RefusesASectionWithoutApiKeyAndProjectId_NamingBoth()
    {
        var configuration = Configuration("""{ "Reporting": { "Testiny": { "Mode": "TestRun" } } }""");

        var exception = Should.Throw<InvalidOperationException>(() => TestinyReporterProvider.Instance.Create(new Services(), configuration.GetSection(TestinyOptions.SectionName), TestData.Run()));

        exception.Message.ShouldBe(
            "ApiKey is empty; set Reporting:Testiny:ApiKey in the user secrets of the test project or in an environment variable (UITESTS_Reporting__Testiny__ApiKey). "
            + "ProjectId is required: the numeric id of the Testiny project (Reporting:Testiny:ProjectId).");
    }

    [Fact]
    public void Create_RefusesModeAutomation_AsNotImplementedYet()
    {
        var configuration = Configuration("""{ "Reporting": { "Testiny": { "ApiKey": "k", "ProjectId": 1, "Mode": "Automation" } } }""");

        var exception = Should.Throw<InvalidOperationException>(() => TestinyReporterProvider.Instance.Create(new Services(), configuration.GetSection(TestinyOptions.SectionName), TestData.Run()));

        exception.Message.ShouldBe("Mode Automation (Testiny's automation API) is not implemented yet; use TestRun.");
    }

    [Theory]
    [InlineData("Reporting:UnmappedTests", "Warn", true)]
    [InlineData("Reporting:UnmappedTests", "fail", true)]
    [InlineData("Reporting:UnmappedTests", "Ignore", false)]
    [InlineData("Reporting:Other", "Warn", false)]
    public async Task Create_ValidatesTheCaseIds_WhenReportingUnmappedTestsIsNotIgnore(string key, string value, bool validates)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Reporting:Testiny:ApiKey"] = "k", ["Reporting:Testiny:ProjectId"] = "1", [key] = value })
            .Build();

        await using var reporter = (TestinyTestRunReporter)TestinyReporterProvider.Instance.Create(new Services(configuration), configuration.GetSection(TestinyOptions.SectionName), TestData.Run());

        reporter.ValidatesCaseIds.ShouldBe(validates);
    }

    [Fact]
    public async Task Create_DoesNotValidateTheCaseIds_WithoutTheRunsConfiguration()
    {
        var configuration = Configuration("""{ "Reporting": { "UnmappedTests": "Warn", "Testiny": { "ApiKey": "k", "ProjectId": 1 } } }""");

        await using var reporter = (TestinyTestRunReporter)TestinyReporterProvider.Instance.Create(new Services(), configuration.GetSection(TestinyOptions.SectionName), TestData.Run());

        reporter.ValidatesCaseIds.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_UsesTheNamedClientOfAnHttpClientFactory()
    {
        var factory = new Factory();
        var configuration = Configuration("""{ "Reporting": { "Testiny": { "ApiKey": "k", "ProjectId": 1 } } }""");

        await using var reporter = TestinyReporterProvider.Instance.Create(new Services(factory: factory), configuration.GetSection(TestinyOptions.SectionName), TestData.Run());

        factory.Names.ShouldBe(["Testiny"]);
    }

    [Fact]
    public void Validate_NamesEveryWrongValue()
    {
        var options = new TestinyOptions
        {
            BaseUrl = "ftp://testiny",
            ApiKey = "k",
            ProjectId = 1,
            TestPlanId = 0,
            MaxAttachmentBytes = 0,
            RequestTimeoutSeconds = 0,
            MinUploadBytesPerSecond = 0,
            ResultForSkipped = "skipped",
            ResultForBlocked = " ",
            RunUrlTemplate = "{AppUrl}/{Project}/testruns/{RunId}",
        };

        options.Validate().ShouldBe(
        [
            "BaseUrl must be an absolute http or https URL such as https://app.testiny.io/api/v1/; it is 'ftp://testiny'.",
            "TestPlanId must be a positive test plan id, or null for no plan.",
            "MaxAttachmentBytes must be positive.",
            "RequestTimeoutSeconds must be at least 1.",
            "MinUploadBytesPerSecond must be positive.",
            "ResultForSkipped must be a result status such as SKIPPED or BLOCKED (upper-case letters, digits and underscores); it is 'skipped'.",
            "ResultForBlocked must be a result status such as SKIPPED or BLOCKED (upper-case letters, digits and underscores); it is ' '.",
            "RunUrlTemplate has the unknown placeholder {Project}; use {AppUrl}, {ProjectId}, {RunId}.",
        ]);
    }

    [Fact]
    public void Defaults_AreTheSpecs()
    {
        var options = new TestinyOptions();

        options.BaseUrl.ShouldBe("https://app.testiny.io/api/v1/");
        options.Mode.ShouldBe(TestinyMode.TestRun);
        options.PlanCasesAsNotRun.ShouldBeTrue();
        options.CloseRunOnComplete.ShouldBeTrue();
        options.Attachments.ShouldBe(TestinyContentPolicy.Failures);
        options.MaxAttachmentBytes.ShouldBe(26_214_400);
        options.Comments.ShouldBe(TestinyContentPolicy.Failures);
        options.ResultForSkipped.ShouldBe("SKIPPED");
        options.ResultForBlocked.ShouldBe("BLOCKED");
        options.RequestTimeoutSeconds.ShouldBe(30);
        options.MinUploadBytesPerSecond.ShouldBe(262_144);
    }

    [Theory]
    [InlineData("https://app.testiny.io/api/v1/", "https://app.testiny.io")]
    [InlineData("http://127.0.0.1:5199/api/v1", "http://127.0.0.1:5199")]
    [InlineData("https://testiny.example.test/tenant/api/v1/", "https://testiny.example.test/tenant")]
    [InlineData("https://testiny.example.test/", "https://testiny.example.test")]
    public void AppUrl_IsTheBaseUrlWithoutTheApiPath(string baseUrl, string appUrl)
    {
        new TestinyOptions { BaseUrl = baseUrl }.AppUrl.ShouldBe(appUrl);
    }

    [Fact]
    public void FormatRunUrl_FollowsTheDefaultTemplate_AndIsNullWithoutOne()
    {
        new TestinyOptions { ProjectId = 42 }.FormatRunUrl(7).ShouldBe(new Uri("https://app.testiny.io/42/testruns/7"));
        new TestinyOptions { ProjectId = 42, RunUrlTemplate = string.Empty }.FormatRunUrl(7).ShouldBeNull();
    }

    private static IConfigurationRoot Configuration(string json)
    {
        return new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
    }

    private sealed class Services(IConfiguration? configuration = null, IHttpClientFactory? factory = null) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IConfiguration) ? configuration
                : serviceType == typeof(IHttpClientFactory) ? factory
                : null;
        }
    }

    private sealed class Factory : IHttpClientFactory
    {
        public List<string> Names { get; } = [];

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            return new HttpClient(new FakeTestiny());
        }
    }
}
