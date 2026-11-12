using _42.Platform.Storyteller.Api.ErrorReporting;

using Sentry;
using Sentry.Protocol;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// What the BeforeSend and BeforeBreadcrumb filter removes, and what it keeps readable.
public class SentryScrubberTests
{
    private const string Filtered = SentryScrubber.Filtered;
    private const string CosmosKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public static TheoryData<string, string> Secrets => new()
    {
        // 1. PEM blocks
        { "key -----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY----- end", $"key {Filtered} end" },
        { "cut -----BEGIN CERTIFICATE-----\nMIIDdzCCAl+gAwIBAgIEAgAA", $"cut {Filtered}" },

        // 2. JWTs
        { "token eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.c2lnbmF0dXJl here", $"token {Filtered} here" },

        // 3. authorization schemes
        { "Authorization: Bearer opaque-token-123", $"Authorization: Bearer {Filtered}" },
        { "ApiKey 2s.YWNtZTpiaWxsaW5n.abc", $"ApiKey {Filtered}" },
        { "Basic dXNlcjpwYXNzd29yZA==", $"Basic {Filtered}" },

        // 4. Storyteller API keys
        { "key 2s.YWNtZTpiaWxsaW5nOmNp.q8Zr3vT1pK0sW9yX2mN5bH7cJ4dF6gL0aE1uI3oP5sY used", $"key {Filtered} used" },

        // 5. name=value secrets
        { $"AccountEndpoint=https://acme.documents.azure.com:443/;AccountKey={CosmosKey};", $"AccountEndpoint=https://acme.documents.azure.com:443/;AccountKey={Filtered};" },
        { "DefaultEndpointsProtocol=https;AccountName=acme;AccountKey=abc123==;EndpointSuffix=core.windows.net", $"DefaultEndpointsProtocol=https;AccountName=acme;AccountKey={Filtered};EndpointSuffix=core.windows.net" },
        { "Endpoint=sb://acme.servicebus.windows.net/;SharedAccessKeyName=root;SharedAccessKey=xyz=", $"Endpoint=sb://acme.servicebus.windows.net/;SharedAccessKeyName=root;SharedAccessKey={Filtered}" },
        { "grant_type=token-exchange&subject_token=abc.def&client_secret=s3cret", $"grant_type=token-exchange&subject_token={Filtered}&client_secret={Filtered}" },
        { "Server=db;User Id=sa;Password=Pa55word!", $"Server=db;User Id=sa;Password={Filtered}" },

        // 6. secret JSON members
        { "{\"Value\":\"s3cret\",\"Protection\":\"Standard\"}", $"{{\"Value\":\"{Filtered}\",\"Protection\":\"Standard\"}}" },
        { "{\"value\": \"a \\\"quoted\\\" secret\"}", $"{{\"value\": \"{Filtered}\"}}" },
        { "{\"password\": 1234, \"user\": \"ada\"}", $"{{\"password\": \"{Filtered}\", \"user\": \"ada\"}}" },
        { "{\"credentials\": {\"key\": \"k\"}}", $"{{\"credentials\": \"{Filtered}\"}}" },
        { "body {\\\"Value\\\":\\\"s3cret\\\"} end", $"body {{\\\"Value\\\":\\\"{Filtered}\\\"}} end" },
        { "{\"protected\":\"eyJhbGciOiJSU0EtT0FFUCJ9\",\"encrypted_key\":\"abc\",\"iv\":\"def\",\"ciphertext\":\"ghi\",\"tag\":\"jkl\"}", $"{{\"protected\":\"{Filtered}\",\"encrypted_key\":\"{Filtered}\",\"iv\":\"{Filtered}\",\"ciphertext\":\"{Filtered}\",\"tag\":\"{Filtered}\"}}" },

        // 7. query strings
        { "GET https://acme.blob.core.windows.net/c/archive.zip?sv=2024&sig=abc rejected", $"GET https://acme.blob.core.windows.net/c/archive.zip?{Filtered} rejected" },
        { "/api/v1/acme/billing/default/key-values/db.password?reveal=true", $"/api/v1/acme/billing/default/key-values/db.password?{Filtered}" },

        // 8. e-mail addresses
        { "Invited ada@example.com to acme", $"Invited {Filtered} to acme" },

        // 9. long opaque tokens
        { $"key {CosmosKey} leaked", $"key {Filtered} leaked" },
        { "secret q8Zr3vT1pK0sW9yX2mN5bH7cJ4dF6gL0aE1uI3oP5sY leaked", $"secret {Filtered} leaked" },
        { "cert MIIDdzCCAl+gAwIBAgIEAgAAuTANBgkqhkiG9w0BAQUFADBaMQswCQYDVQQGEwJJRTESMBAGA1UEChMJQmFsdGltb3Jl", $"cert {Filtered}" },
    };

    public static TheoryData<string> Names => new()
    {
        "No Administrator access to 'acme.web'.",
        "The configuration acme.billing.default.cfg.payments doesn't exist.",
        "The key Payments_Gateway_ApiKey_Version2 has no version 3.",
        "ConnectionStrings_PrimaryDatabase2 was protected.",
        "Activity 3f2c9a1e-7d4b-4c8a-9e1f-0b6a5d2c8e7f failed with 503.",
        "Thumbprint 9F8E7D6C5B4A39281706F5E4D3C2B1A09F8E7D6C failed.",
        "Signing keys could not be retrieved for /api/v1/acme/billing/default/key-values/db.password (HttpRequestException)",
        "The Bearer token was rejected; ApiKey authentication is off.",
        "Function 'SetKeyValue', Invocation id 'invocation': An exception was thrown by the invocation.",
        "The view transfer 7d4b0b6a is not valid: {\"Organization\":\"acme\",\"Project\":\"billing\"}",
    };

    [Theory]
    [MemberData(nameof(Secrets))]
    public void ScrubText_ASecret_IsFiltered(string text, string expected)
    {
        SentryScrubber.ScrubText(text).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void ScrubText_NamesOnly_StayReadable(string text)
    {
        SentryScrubber.ScrubText(text).ShouldBe(text);
    }

    [Fact]
    public void ScrubEvent_ClearsEverythingButTheAllowedParts()
    {
        var sentryEvent = new SentryEvent
        {
            Message = new SentryMessage { Message = "Writing {Configuration} failed: {Detail}", Formatted = "Writing acme.billing failed: Password=s3cret" },
            ServerName = "machine-01",
            User = new SentryUser { Id = "installation", Email = "ada@example.com", Username = "ada" },
            SentryExceptions =
            [
                new SentryException
                {
                    Value = $"AccountKey={CosmosKey}",
                    Mechanism = new Mechanism { Description = "token=abc123", HelpLink = "https://help.example/?token=abc123" },
                },
            ],
        };
        sentryEvent.SentryExceptions!.Single().Mechanism!.Data["HResult"] = "0x80131500";
        sentryEvent.SentryExceptions!.Single().Mechanism!.Data["plain"] = "s3cret";
        sentryEvent.Request.Url = "https://api.2s.42for.net/api/v1/acme?reveal=true";
        sentryEvent.Request.Headers["Authorization"] = "Bearer abc";
        sentryEvent.SetTag(SentryScrubber.FunctionTag, "SetKeyValue");
        sentryEvent.SetTag("Detail", "Password=s3cret");
        sentryEvent.SetTag("RequestPath", "/api/v1/acme");
        sentryEvent.SetExtra("state", new { Secret = "s3cret" });
        sentryEvent.Contexts["leak"] = new { Secret = "s3cret" };
        sentryEvent.Contexts[SentryScrubber.FunctionContext] = new Dictionary<string, string> { ["name"] = "SetKeyValue" };

        SentryScrubber.ScrubEvent(sentryEvent);

        sentryEvent.Message!.Formatted.ShouldBe($"Writing acme.billing failed: Password={Filtered}");
        sentryEvent.Message.Message.ShouldBe("Writing {Configuration} failed: {Detail}");
        sentryEvent.ServerName.ShouldBeNull();
        sentryEvent.User.Id.ShouldBeNull();
        sentryEvent.User.Email.ShouldBeNull();
        sentryEvent.User.Username.ShouldBeNull();
        sentryEvent.Request.Url.ShouldBeNull();
        sentryEvent.Request.Headers.ShouldBeEmpty();
        sentryEvent.Tags.Keys.ShouldBe(new[] { SentryScrubber.FunctionTag });
        sentryEvent.Extra["state"].ShouldBe(Filtered);
        sentryEvent.Contexts.ContainsKey("leak").ShouldBeFalse();
        sentryEvent.Contexts.ContainsKey(SentryScrubber.FunctionContext).ShouldBeTrue();

        var exception = sentryEvent.SentryExceptions!.Single();
        exception.Value.ShouldBe($"AccountKey={Filtered}");
        exception.Mechanism!.Description.ShouldBe($"token={Filtered}");
        exception.Mechanism.HelpLink.ShouldBeNull();
        exception.Mechanism.Data.Keys.ShouldBe(new[] { "HResult" });
    }

    [Fact]
    public void ScrubBreadcrumb_TextsAreScrubbed()
    {
        var breadcrumb = new Breadcrumb(
            "Retrying with AccountKey=abc123",
            "default",
            new Dictionary<string, string> { ["exception_message"] = "token=abc123 for ada@example.com", ["eventId"] = "7" },
            "_42.Platform.Storyteller.Api.V1.KeyValuesHttp",
            BreadcrumbLevel.Warning);

        var scrubbed = SentryScrubber.ScrubBreadcrumb(breadcrumb).ShouldNotBeNull();

        scrubbed.Message.ShouldBe($"Retrying with AccountKey={Filtered}");
        scrubbed.Data!["exception_message"].ShouldBe($"token={Filtered} for {Filtered}");
        scrubbed.Data["eventId"].ShouldBe("7");
        scrubbed.Category.ShouldBe(breadcrumb.Category);
        scrubbed.Level.ShouldBe(BreadcrumbLevel.Warning);
    }

    [Fact]
    public void ScrubBreadcrumb_AnOutgoingRequest_IsDropped()
    {
        var breadcrumb = new Breadcrumb(string.Empty, "http", new Dictionary<string, string> { ["url"] = "https://api.workos.com/x?token=abc" });

        SentryScrubber.ScrubBreadcrumb(breadcrumb).ShouldBeNull();
    }

    [Fact]
    public void CompleteFunction_TheWorkersEntry_GetsTheTransaction()
    {
        var sentryEvent = new SentryEvent();
        sentryEvent.SetTag("functionName", "SetKeyValue");
        sentryEvent.SetTag("invocationId", "invocation-9");

        SentryScrubber.CompleteFunction(sentryEvent);
        SentryScrubber.ScrubEvent(sentryEvent);

        sentryEvent.TransactionName.ShouldBe("PUT /api/v1/{organization}/{project}/{view}/key-values/{key}");
        var tag = sentryEvent.Tags.ShouldHaveSingleItem();
        tag.Key.ShouldBe(SentryScrubber.FunctionTag);
        tag.Value.ShouldBe("SetKeyValue");
        var context = sentryEvent.Contexts[SentryScrubber.FunctionContext].ShouldBeOfType<Dictionary<string, string>>();
        context["invocation_id"].ShouldBe("invocation-9");
    }

    [Fact]
    public void CompleteFunction_AnEventWithATransaction_IsKept()
    {
        var sentryEvent = new SentryEvent { TransactionName = "GET /api/v1/{organization}" };
        sentryEvent.SetTag("functionName", "SetKeyValue");

        SentryScrubber.CompleteFunction(sentryEvent);

        sentryEvent.TransactionName.ShouldBe("GET /api/v1/{organization}");
    }
}
