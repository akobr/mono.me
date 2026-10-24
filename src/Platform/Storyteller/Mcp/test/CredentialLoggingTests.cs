using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// Machine credentials reach the tool result, and nothing else: no log of the host carries them.
public sealed class CredentialLoggingTests : IDisposable
{
    private const string AccessKey = "2s.ACCESS-KEY-MARKER-7f3a";
    private const string CertificatePassword = "CERT-PASSWORD-MARKER-91c2";
    private const string Certificate = "CERTIFICATE-MARKER-MIIK";

    private readonly McpTestHost _host = new();

    [Fact]
    public async Task CreateMachine_ReturnsTheCredentials_AndLogsNoneOfThem()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(
            $$"""{"Id":"m-1","ObjectId":"m-1","AccessKey":"{{AccessKey}}","Scopes":["AnnotationRead","ConfigurationRead"],"CredentialKind":"CertificateAndApiKey","Certificate":"{{Certificate}}","CertificatePassword":"{{CertificatePassword}}"}""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("create_machine", new { organization = "house", project = "main", scopes = new[] { "ConfigurationRead" }, credentialKind = "CertificateAndApiKey" });

        result.IsError.ShouldNotBe(true);
        result.Text().ShouldStartWith("The API stores no copy of the API key in AccessKey");
        result.Text().ShouldContain(AccessKey);
        result.Text().ShouldContain(CertificatePassword);
        result.StructuredContent!.Value.GetProperty(MachineCredentialNotice.PropertyName).GetString().ShouldNotBeNullOrEmpty();

        var request = _host.Api.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsolutePath.ShouldBe("/api/v1/house/main/access/machines");
        request.Body.ShouldNotBeNull();
        request.Body.ShouldContain("\"Organization\":\"house\"");
        request.Body.ShouldContain("\"Project\":\"main\"");
        request.Body.ShouldContain("\"Scopes\":[\"ConfigurationRead\"]");

        ShouldNotBeLogged();
    }

    [Fact]
    public async Task ResetMachine_ReturnsTheNewKey_AndDoesNotLogIt()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json($$"""{"Id":"m-1","AccessKey":"{{AccessKey}}","CredentialKind":"ApiKey"}""");
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("reset_machine", new { organization = "house", project = "main", id = "m-1" });

        result.Text().ShouldContain(AccessKey);
        _host.Api.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Put);
        ShouldNotBeLogged();
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private void ShouldNotBeLogged()
    {
        _host.Logs.Entries.ShouldNotBeEmpty();
        _host.Logs.Entries.ShouldNotContain(entry => entry.Contains(AccessKey, StringComparison.Ordinal));
        _host.Logs.Entries.ShouldNotContain(entry => entry.Contains(CertificatePassword, StringComparison.Ordinal));
        _host.Logs.Entries.ShouldNotContain(entry => entry.Contains(Certificate, StringComparison.Ordinal));
    }
}
