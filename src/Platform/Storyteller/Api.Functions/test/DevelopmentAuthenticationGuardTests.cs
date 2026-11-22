using System.Reflection;
using System.Runtime.ExceptionServices;

using _42.Platform.Storyteller.Api.Security;

using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// A Debug build (DEV_AUTH) skips the checks of users, so it starts only in Development and outside the hosting platforms;
// a Release build always starts (424-242). The decision takes the build as a parameter, so both branches run in either
// build; the wiring of Program.cs is checked through the app's entry point in a Debug build.
public class DevelopmentAuthenticationGuardTests
{
    private static readonly Func<string, string?> NoVariables = static _ => null;

    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    public void DebugBuild_InDevelopment_Starts(string environment)
    {
        DevelopmentAuthenticationGuard.GetRefusal(environment, NoVariables, developmentAuthentication: true).ShouldBeNull();
        Should.NotThrow(() => DevelopmentAuthenticationGuard.Ensure(new TestHostEnvironment(environment), NoVariables, developmentAuthentication: true));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("Developmental")]
    [InlineData("")]
    public void DebugBuild_OutsideDevelopment_IsRefused(string environment)
    {
        var refusal = DevelopmentAuthenticationGuard.GetRefusal(environment, NoVariables, developmentAuthentication: true);

        refusal.ShouldNotBeNull();
        refusal.ShouldStartWith("DEV_AUTH: ");
        refusal.ShouldContain($"the host environment is '{environment}'");
        refusal.ShouldContain("dotnet publish -c Release");

        var exception = Should.Throw<InvalidOperationException>(
            () => DevelopmentAuthenticationGuard.Ensure(new TestHostEnvironment(environment), NoVariables, developmentAuthentication: true));
        exception.Message.ShouldBe(refusal);
    }

    [Fact]
    public void DebugBuild_WithoutAnEnvironmentName_IsRefused()
    {
        DevelopmentAuthenticationGuard.GetRefusal(null, NoVariables, developmentAuthentication: true).ShouldNotBeNull();
    }

    // A deployment whose environment was set to Development is still a hosting platform.
    [Theory]
    [InlineData("WEBSITE_SITE_NAME")]
    [InlineData("WEBSITE_INSTANCE_ID")]
    [InlineData("WEBSITE_POD_NAME")]
    [InlineData("LEGION_SERVICE_HOST")]
    [InlineData("CONTAINER_APP_NAME")]
    [InlineData("KUBERNETES_SERVICE_HOST")]
    public void DebugBuild_InDevelopmentOnAHostingPlatform_IsRefused(string variable)
    {
        Func<string, string?> variables = name => name == variable ? "prod-2s-func" : null;

        var refusal = DevelopmentAuthenticationGuard.GetRefusal("Development", variables, developmentAuthentication: true);

        refusal.ShouldNotBeNull();
        refusal.ShouldStartWith("DEV_AUTH: ");
        refusal.ShouldContain($"{variable} is set");
        refusal.ShouldNotContain("prod-2s-func");
        Should.Throw<InvalidOperationException>(
            () => DevelopmentAuthenticationGuard.Ensure(new TestHostEnvironment("Development"), variables, developmentAuthentication: true));
    }

    // Core Tools sets WEBSITE_HOSTNAME (localhost:<port>), which is not a hosting variable; an empty variable is not set.
    [Theory]
    [InlineData("WEBSITE_HOSTNAME", "localhost:7071")]
    [InlineData("WEBSITE_SITE_NAME", "")]
    public void DebugBuild_InDevelopmentWithOtherVariables_Starts(string variable, string value)
    {
        Func<string, string?> variables = name => name == variable ? value : null;

        DevelopmentAuthenticationGuard.GetRefusal("Development", variables, developmentAuthentication: true).ShouldBeNull();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void ReleaseBuild_AlwaysStarts(string environment)
    {
        Func<string, string?> hosted = name => name == "WEBSITE_SITE_NAME" ? "prod-2s-func" : null;

        DevelopmentAuthenticationGuard.GetRefusal(environment, NoVariables, developmentAuthentication: false).ShouldBeNull();
        DevelopmentAuthenticationGuard.GetRefusal(environment, hosted, developmentAuthentication: false).ShouldBeNull();
        Should.NotThrow(() => DevelopmentAuthenticationGuard.Ensure(new TestHostEnvironment(environment), hosted, developmentAuthentication: false));
    }

    // The overload of Program.cs takes the build of this assembly: DEV_AUTH exactly in a Debug build.
    [Fact]
    public void Ensure_OfTheProcess_FollowsTheBuild()
    {
#if DEBUG
        DevAuth.ShouldBeTrue();
#else
        DevAuth.ShouldBeFalse();
#endif

        if (DevAuth)
        {
            Should.Throw<InvalidOperationException>(() => DevelopmentAuthenticationGuard.Ensure(new TestHostEnvironment(Environments.Production)));
        }
        else
        {
            Should.NotThrow(() => DevelopmentAuthenticationGuard.Ensure(new TestHostEnvironment(Environments.Production)));
        }
    }

    // Program.cs calls the guard before it registers anything: in a Debug build its entry point, run in a process that is
    // not in Development (the default of a test run: no environment variable gives Production), fails in Build() with the
    // guard's message. A Release build would start the host there, so the check runs in Debug only.
    [Fact]
    public void EntryPoint_OfADebugBuildOutsideDevelopment_RefusesToStart()
    {
        if (!DevAuth)
        {
            Assert.Skip("A Release build starts: the entry point would run the host.");
        }

        if (IsDevelopment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT"))
            && DevelopmentAuthenticationGuard.HostingVariables.All(name => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))))
        {
            Assert.Skip("The test process runs in Development, where a Debug build starts.");
        }

        var entryPoint = typeof(DevelopmentAuthenticationGuard).Assembly.EntryPoint.ShouldNotBeNull();
        object?[] arguments = entryPoint.GetParameters().Length == 0 ? [] : [Array.Empty<string>()];

        var exception = Should.Throw<InvalidOperationException>(() => Invoke(entryPoint, arguments));

        exception.Message.ShouldStartWith("DEV_AUTH: ");
        exception.StackTrace.ShouldNotBeNull().ShouldContain(nameof(DevelopmentAuthenticationGuard));
    }

    private static bool IsDevelopment(string? environment)
    {
        return string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase);
    }

    // The entry point of top-level statements with await waits for them; what they throw comes out of reflection wrapped.
    private static void Invoke(MethodInfo entryPoint, object?[] arguments)
    {
        try
        {
            var result = entryPoint.Invoke(null, arguments);

            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }
}
