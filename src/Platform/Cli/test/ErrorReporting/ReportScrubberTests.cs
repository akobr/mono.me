using System.Collections.Generic;
using System.Linq;
using _42.Platform.Cli.ErrorReporting;
using Sentry;
using Sentry.Protocol;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.ErrorReporting;

// What the reports keep of a text: never a value given on the command line, never a secret; in Sentry no customer data.
public class ReportScrubberTests
{
    private static readonly string[] NoValues = [];

    [Theory]
    [InlineData("Cannot write s3cr3t-value to the store.", "s3cr3t-value", "Cannot write *** to the store.")]
    [InlineData("Cannot write S3CR3T-VALUE to the store.", "s3cr3t-value", "Cannot write *** to the store.")]
    [InlineData("{\"path\":\"C:\\\\out\\\\a.json\"}", "C:\\out\\a.json", "{\"path\":\"***\"}")]
    [InlineData("GET /v1/kv/acme%20corp failed", "acme corp", "GET /v1/kv/*** failed")]
    [InlineData("view v1 of /api/v1x", "v1", "view *** of /api/v1x")]
    public void ScrubSecrets_ReplacesAGivenValue_InEveryForm(string text, string value, string expected)
    {
        ReportScrubber.ScrubSecrets(text, [value]).ShouldBe(expected);
    }

    [Fact]
    public void ScrubSecrets_ReplacesTheLongestValueFirst()
    {
        ReportScrubber.ScrubSecrets("acme.billing and acme", ["acme", "acme.billing"]).ShouldBe("*** and ***");
    }

    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl", "eyJ")]
    [InlineData("Authorization: Basic dXNlcjpwYXNzd29yZA==", "dXNlcjpwYXNzd29yZA")]
    [InlineData("key 2s.YWNtZTpiaWxsaW5nOm0x.AbCdEfGhIjKlMnOpQrStUv", "AbCdEfGhIjKlMnOpQrStUv")]
    [InlineData("Server=x;Password=hunter2;", "hunter2")]
    [InlineData("{\"value\":\"s3cr3t\",\"key\":\"db\"}", "s3cr3t")]
    [InlineData("body: {\\\"value\\\":\\\"s3cr3t\\\"}", "s3cr3t")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg\n-----END PRIVATE KEY-----", "MIIEvQIBADANBg")]
    [InlineData("token Zm9vYmFyYmF6cXV4MTIzNDU2Nzg5MGFiY2RlZmdoaWprbG1ub3A=", "Zm9vYmFyYmF6cXV4")]
    public void ScrubSecrets_RemovesTheShapesOfSecrets(string text, string secret)
    {
        var scrubbed = ReportScrubber.ScrubSecrets(text, NoValues);

        scrubbed.ShouldNotContain(secret);
        scrubbed.ShouldContain(ReportScrubber.Filtered);
    }

    [Fact]
    public void ScrubSecrets_KeepsNamesAndProse()
    {
        const string text = "The key db.password of exe.northwind.invoicing.prod has no Basic information at /api/v1/organizations/acme.";

        ReportScrubber.ScrubSecrets(text, NoValues).ShouldBe(text);
    }

    [Fact]
    public void ScrubForSentry_KeepsTheFirstLine()
    {
        const string text = "The HTTP status code of the response was not expected (302).\n\nStatus: 302\nResponse: \n{\"connection\":\"Server=x\"}";

        ReportScrubber.ScrubForSentry(text, NoValues).ShouldBe("The HTTP status code of the response was not expected (302).");
    }

    [Theory]
    [InlineData("Could not find file 'C:\\Users\\ada\\out.zip'.", "Could not find file '***'.")]
    [InlineData("'notanumber' is not a valid number.", "'***' is not a valid number.")]
    [InlineData("The process can't access the file \"x.json\" because it's in use.", "The process can't access the file \"***\" because it's in use.")]
    [InlineData("Access to the path C:\\Users\\ada\\secret is denied.", "Access to the path *** is denied.")]
    [InlineData("Access to the path \\\\server\\share\\a.txt is denied.", "Access to the path *** is denied.")]
    [InlineData("No file at /home/ada/.sform/out.json here", "No file at *** here")]
    [InlineData("No file at ~/out.json here", "No file at *** here")]
    [InlineData("Call https://user:pw@api.acme.example:8443/api/v1/organizations/acme?x=1 failed", "Call https://api.acme.example:8443/*** failed")]
    [InlineData("Call https://api.acme.example failed", "Call https://api.acme.example failed")]
    [InlineData("Signed in as ada@acme.example", "Signed in as ***")]
    [InlineData("AADSTS700016: application 2f6a1c3e-9a4b-4d1e-8f7a-0b1c2d3e4f5a was not found", "AADSTS700016: application *** was not found")]
    [InlineData("Request to /v1/organizations/acme/projects/billing failed", "Request to *** failed")]
    [InlineData("1/2 of the keys and/or values", "1/2 of the keys and/or values")]
    public void ScrubForSentry_RemovesCustomerData(string text, string expected)
    {
        ReportScrubber.ScrubForSentry(text, NoValues).ShouldBe(expected);
    }

    [Fact]
    public void ScrubForSentry_RemovesTheValuesOfTheRun_AndKeepsTheShape()
    {
        const string text = "The machine credentials in STORYTELLER_API_KEY belong to acme.proj, not acme.billing.";

        ReportScrubber.ScrubForSentry(text, ["acme.billing", "acme", "billing", "acme.proj", "proj"])
            .ShouldBe("The machine credentials in STORYTELLER_API_KEY belong to ***, not ***.");
    }

    [Fact]
    public void ScrubForSentry_CutsALongMessage()
    {
        var scrubbed = ReportScrubber.ScrubForSentry(string.Join(' ', Enumerable.Repeat("word", 200)), NoValues);

        scrubbed.Length.ShouldBe(ReportScrubber.MaxSentryMessageLength);
        scrubbed.ShouldEndWith("…");
    }

    [Fact]
    public void NoText_StaysEmpty()
    {
        ReportScrubber.ScrubSecrets(null, NoValues).ShouldBeEmpty();
        ReportScrubber.ScrubForSentry(string.Empty, NoValues).ShouldBeEmpty();
    }

    [Fact]
    public void ScrubEvent_KeepsWhatAReportMayCarry()
    {
        var sentryEvent = new SentryEvent
        {
            ServerName = "ADA-LAPTOP",
            User = new SentryUser { Id = "aa:bb:cc:dd:ee:ff", Username = "ada" },
            SentryExceptions =
            [
                new SentryException
                {
                    Type = "System.IO.FileNotFoundException",
                    Value = "Could not find file 'C:\\Users\\ada\\s3cr3t.txt'.\nFile name: 'C:\\Users\\ada\\s3cr3t.txt'",
                    Mechanism = new Mechanism { HelpLink = "https://help.example/s3cr3t", Description = "s3cr3t" },
                },
            ],
            DebugImages = [new DebugImage { CodeFile = "C:\\Users\\ada\\.dotnet\\tools\\.store\\42.platform.cli\\42.Platform.Cli.dll" }],
        };
        sentryEvent.SentryExceptions!.Single().Mechanism!.Data["HResult"] = "0x80070002";
        sentryEvent.SentryExceptions!.Single().Mechanism!.Data["key"] = "s3cr3t";
        sentryEvent.SetTag(ReportScrubber.CommandTag, "sform storyteller kv set");
        sentryEvent.SetTag("key", "s3cr3t");
        sentryEvent.SetExtra("Exception[0][sentry:extra]", "s3cr3t");
        sentryEvent.Contexts["os"] = new Dictionary<string, object> { ["name"] = "Windows" };
        sentryEvent.Contexts["leak"] = new Dictionary<string, object> { ["value"] = "s3cr3t" };

        var scrubbed = ReportScrubber.ScrubEvent(sentryEvent, ["s3cr3t"], "3f1c2a9e5b7d4c119e0a2d4f6b8c1a07");

        var exception = scrubbed.SentryExceptions!.Single();
        exception.Value.ShouldBe("Could not find file '***'.");
        exception.Type.ShouldBe("System.IO.FileNotFoundException");
        exception.Mechanism!.HelpLink.ShouldBeNull();
        exception.Mechanism.Description.ShouldBe("***");
        exception.Mechanism.Data.Keys.ShouldBe(["HResult"]);
        scrubbed.DebugImages!.Single().CodeFile.ShouldBe("42.Platform.Cli.dll");
        scrubbed.Tags.Keys.ShouldBe([ReportScrubber.CommandTag]);
        scrubbed.Extra.Values.ShouldAllBe(value => Equals(value, ReportScrubber.Filtered));
        scrubbed.Contexts.Keys.ShouldContain("os");
        scrubbed.Contexts.Keys.ShouldNotContain("leak");
        scrubbed.User.Id.ShouldBe("3f1c2a9e5b7d4c119e0a2d4f6b8c1a07");
        scrubbed.User.Username.ShouldBeNull();
        scrubbed.ServerName.ShouldBeNull();
    }

    [Fact]
    public void ScrubEvent_WithoutAnInstallationId_HasNoUser()
    {
        var sentryEvent = new SentryEvent { User = new SentryUser { Id = "aa:bb:cc:dd:ee:ff" } };

        ReportScrubber.ScrubEvent(sentryEvent, NoValues, null).User.Id.ShouldBeNull();
    }
}
