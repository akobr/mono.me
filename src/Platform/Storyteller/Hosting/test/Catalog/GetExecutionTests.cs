using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Catalog;

public sealed class GetExecutionTests : IDisposable
{
    private const string Contoso = "exe.contoso.invoicing.dev";

    private readonly HostingFixture _fixture = new();

    [Fact]
    public async Task AnExecution_IsReadInAnyStatus_WithInactiveAndDeletedOnes()
    {
        _fixture.RouteAnnotation(Contoso, json => json["IsDisabled"] = true);

        var execution = (await Catalog().GetExecutionAsync(Contoso)).ShouldNotBeNull();

        execution.Key.ShouldBe($"{HostingFixture.Scope}.{Contoso}");
        execution.SubjectName.ShouldBe("contoso");
        execution.GetStatus(_fixture.Time.GetUtcNow()).ShouldBe(AnnotationStatus.Disabled);
        var request = _fixture.Http.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(Contoso));
        request.Uri.Query.ShouldBe("?includeInactive=true&includeDeleted=true");
    }

    [Fact]
    public async Task AFullKey_NamesItsOwnScope()
    {
        _fixture.Route("/api/v1/acme/payroll/staging/annotations/exe.contoso.invoicing.dev", () => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(Contoso)));

        var execution = (await Catalog().GetExecutionAsync("@acme.payroll.staging.exe.contoso.invoicing.dev")).ShouldNotBeNull();

        execution.Key.ShouldBe("@acme.payroll.staging.exe.contoso.invoicing.dev");
    }

    [Fact]
    public async Task AMissingExecution_IsNull()
    {
        _fixture.RouteStatus(Contoso, HttpStatusCode.NotFound);

        (await Catalog().GetExecutionAsync(Contoso)).ShouldBeNull();
    }

    [Fact]
    public async Task ADeletedExecution_IsNull()
    {
        _fixture.RouteAnnotation(Contoso, TestAnnotations.Deleted());

        (await Catalog().GetExecutionAsync(Contoso)).ShouldBeNull();
    }

    [Theory]
    [InlineData("usg.contoso.invoicing")]
    [InlineData("@acme.billing.default.rst.invoicing")]
    [InlineData("uxe.contoso.invoicing.dev.daily")]
    public async Task AKeyThatIsNotAnExecution_IsRefused(string key)
    {
        var exception = await Should.ThrowAsync<ArgumentException>(() => Catalog().GetExecutionAsync(key));

        exception.ParamName.ShouldBe("key");
        exception.Message.ShouldContain("exe.{subject}.{responsibility}.{context}");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("exe.contoso")]
    [InlineData("@acme.billing.exe.contoso.invoicing.dev")]
    public async Task AKeyThatDoesNotParse_IsRefused(string key)
    {
        await Should.ThrowAsync<ArgumentException>(() => Catalog().GetExecutionAsync(key));

        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAnnotationKey_WithoutTheOptionsKey_IsRefused()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");

        var exception = await Should.ThrowAsync<ArgumentException>(() => Catalog().GetExecutionAsync(Contoso));

        exception.Message.ShouldContain("STORYTELLER_KEY");
    }

    [Fact]
    public async Task AFullKey_NeedsNoOptionsKey()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");
        _fixture.RouteAnnotation(Contoso);

        (await Catalog().GetExecutionAsync($"{HostingFixture.Scope}.{Contoso}")).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, TestAnnotations.ErrorJson, "it needs Annotation.Read on the project", false)]
    [InlineData(HttpStatusCode.Forbidden, TestAnnotations.RestrictedJson, "a machine restricted to an annotation reads that annotation and its descendants only", false)]
    [InlineData(HttpStatusCode.Unauthorized, null, "refused the credentials", false)]
    [InlineData(HttpStatusCode.InternalServerError, TestAnnotations.ErrorJson, "failed", true)]
    [InlineData(HttpStatusCode.OK, "<html>Sign in to the Wi-Fi</html>", "did not parse as an annotation", true)]
    public async Task OtherFailures_ThrowWithTheCatalogsWords(HttpStatusCode status, string? body, string words, bool isTransient)
    {
        _fixture.RouteStatus(Contoso, status, body);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog().GetExecutionAsync(Contoso));

        exception.Key.ShouldBe($"{HostingFixture.Scope}.{Contoso}");
        exception.StatusCode.ShouldBe((int)status);
        exception.IsTransient.ShouldBe(isTransient);
        exception.Message.ShouldContain(words);
        exception.Message.ShouldNotContain("Wi-Fi");
    }

    [Fact]
    public async Task ACredentialThatCannotAuthorize_Throws()
    {
        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(
            () => Catalog(options => options.Credential = TestCredential.Failing(StorytellerAuthenticationFailureReason.Unavailable)).GetExecutionAsync(Contoso));

        exception.IsTransient.ShouldBeTrue();
        exception.Message.ShouldContain("Unavailable");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AReadThatOutlivesTheReadTimeout_IsTransient()
    {
        _fixture.Http.Respond(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Not cancelled.");
        });

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(
            () => Catalog(options => options.ReadTimeout = TimeSpan.FromMilliseconds(100)).GetExecutionAsync(Contoso));

        exception.IsTransient.ShouldBeTrue();
        exception.Message.ShouldContain("ReadTimeout");
    }

    [Fact]
    public async Task TheCallersCancellation_Escapes()
    {
        var gate = new GatedResponse(() => StubHttpHandler.Response(HttpStatusCode.OK, TestAnnotations.Json(Contoso)));
        _fixture.Http.Respond(gate.RespondAsync);
        using var cancellation = new CancellationTokenSource();

        var read = Catalog().GetExecutionAsync(Contoso, cancellation.Token);
        await gate.Entered;
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => read);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private ISubscriptionCatalog Catalog(Action<StorytellerConfigurationOptions>? configure = null)
    {
        return _fixture.BuildServices(configure).GetRequiredService<ISubscriptionCatalog>();
    }
}
