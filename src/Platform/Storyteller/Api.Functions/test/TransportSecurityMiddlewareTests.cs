using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class TransportSecurityMiddlewareTests
{
    [Theory]
    [InlineData("https://api.example.com/api/x", null, true)]
    [InlineData("http://api.example.com/api/x", null, false)]
    [InlineData("http://internal/api/x", "https", true)]
    [InlineData("http://internal/api/x", "https, http", true)]
    [InlineData("http://internal/api/x", "http", false)]
    public void IsHttps_LooksAtTheSchemeAndTheForwardedProtocol(string url, string? forwardedProto, bool expected)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = forwardedProto is null ? null : new Dictionary<string, string> { ["X-Forwarded-Proto"] = forwardedProto };
        var (context, _) = FunctionTestDoubles.CreateRequest(services, headers);
        var request = new TestHttpRequestData(context, headers, url: new Uri(url));

        TransportSecurityMiddleware.IsHttps(request).ShouldBe(expected);
    }

    [Fact]
    public async Task PlainHttp_IsRefusedOutsideDevelopment()
    {
        var middleware = new TransportSecurityMiddleware(new FakeEnvironment(Environments.Production));
        var (context, _) = CreatePlainRequest();

        var act = () => middleware.Invoke(context, _ => Task.CompletedTask);

        var exception = await act.ShouldThrowAsync<InvalidInputException>();
        exception.ErrorCode.ShouldBe(ErrorCodes.HttpsRequired);
    }

    [Fact]
    public async Task PlainHttp_IsAllowedInDevelopment()
    {
        var middleware = new TransportSecurityMiddleware(new FakeEnvironment(Environments.Development));
        var (context, _) = CreatePlainRequest();
        var called = false;

        await middleware.Invoke(context, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        called.ShouldBeTrue();
    }

    [Fact]
    public async Task Https_PassesInProduction()
    {
        var middleware = new TransportSecurityMiddleware(new FakeEnvironment(Environments.Production));
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, _) = FunctionTestDoubles.CreateRequest(services);
        var called = false;

        await middleware.Invoke(context, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        called.ShouldBeTrue();
    }

    private static (TestFunctionContext Context, TestHttpRequestData Request) CreatePlainRequest()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestFunctionContext(services);
        var request = new TestHttpRequestData(context, url: new Uri("http://api.example.com/api/x"));
        ((TestInvocationFeatures)context.Features).Set<IHttpRequestDataFeature>(new TestHttpRequestFeature(request));
        return (context, request);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
