using System.Reflection;
using System.Security.Claims;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class BindingsHttpTests
{
    [Fact]
    public async Task GetBindingCatalog_ReturnsTheRegisteredBindings()
    {
        var executor = new BindingExecutor();
        executor.RegisterFunction("config", new ConfigBindingFunction());
        executor.RegisterSource("primaryVault", new ThrowingSource());
        var http = new BindingsHttp(ReaderAccess(), executor);

        var result = await http.GetBindingCatalog(ReaderRequest(), "acme", "billing");

        var catalog = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<BindingCatalog>();
        catalog.Functions.Select(function => function.Name).ShouldBe(["config"]);
        catalog.Functions[0].Examples.ShouldNotBeEmpty();
        catalog.Sources.Select(source => source.Name).ShouldBe(["primaryVault"]);
        catalog.Sources[0].IsSecret.ShouldBeFalse();
    }

    [Fact]
    public async Task GetBindingCatalog_WithoutBindings_ReturnsAnEmptyCatalog()
    {
        var http = new BindingsHttp(ReaderAccess());

        var result = await http.GetBindingCatalog(ReaderRequest(), "acme", "billing");

        var catalog = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<BindingCatalog>();
        catalog.Functions.ShouldBeEmpty();
        catalog.Sources.ShouldBeEmpty();
    }

    [Fact]
    public void AddConfigurationBindings_RegistersTheExecutorAsTheCatalog()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ConfigBindingFunction>();
        services.AddConfigurationBindings(options => options.AddFunction<ConfigBindingFunction>("config"));
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<IBindingCatalog>();

        catalog.ShouldBeSameAs(provider.GetRequiredService<IBindingExecutor>());
        catalog.GetCatalog().Functions.Select(function => function.Name).ShouldBe(["config"]);
    }

    private static TestHttpRequestData ReaderRequest()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "user-1"), new("scp", Scopes.Configuration.Read) };
        return request;
    }

    // Answers the role check of a Release build; every other call fails the test.
    private static IAccessService ReaderAccess()
    {
        return DispatchProxy.Create<IAccessService, ReaderAccessProxy>();
    }

    public class ReaderAccessProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name == nameof(IAccessService.GetAccountRoleAsync)
                ? Task.FromResult(AccountRole.Reader)
                : throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
        }
    }

    private sealed class ThrowingSource : IBindingSource
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
        {
            throw new InvalidOperationException("The catalog must not resolve values.");
        }
    }
}
