using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Api.Security;

// A Debug build (DEV_AUTH) decodes bearer tokens without checking them, skips User.Impersonation and the role check of
// users, gives every user the secret rights and serves requests without an identity. With every organization in one
// container that opens the data of all of them, so such a build starts only in the Development environment, which Core
// Tools (func start, also under Aspire) sets, and outside the hosting platforms, whose variables stand in for "the host is
// loopback", which the worker cannot see (it always listens on localhost behind the host). Release builds always start
// (424-242).
internal static class DevelopmentAuthenticationGuard
{
    public const string Advice = "Deploy a Release build (dotnet publish -c Release), or run a Debug build only locally, under func start or Aspire.";

    // Set on the instances of a hosting platform, never by Core Tools or Aspire: WEBSITE_SITE_NAME on App Service and every
    // Azure Functions plan, WEBSITE_INSTANCE_ID on App Service plans and Windows Consumption, WEBSITE_POD_NAME and
    // LEGION_SERVICE_HOST on Flex and Linux Consumption, CONTAINER_APP_NAME on Container Apps, KUBERNETES_SERVICE_HOST in
    // every Kubernetes pod (AKS, KEDA). They refuse a deployment there even when its environment was set to Development; on
    // any other host (a VM, plain Docker, Container Instances) only the environment name refuses a Debug build.
    public static readonly IReadOnlyList<string> HostingVariables =
    [
        "WEBSITE_SITE_NAME",
        "WEBSITE_INSTANCE_ID",
        "WEBSITE_POD_NAME",
        "LEGION_SERVICE_HOST",
        "CONTAINER_APP_NAME",
        "KUBERNETES_SERVICE_HOST",
    ];

    // Program.cs: the build of the process, its environment and its variables.
    public static void Ensure(IHostEnvironment environment)
    {
        Ensure(environment, Environment.GetEnvironmentVariable, HttpRequestDataExtensions.IsDevelopmentAuthentication);
    }

    internal static void Ensure(IHostEnvironment environment, Func<string, string?> getVariable, bool developmentAuthentication)
    {
        var refusal = GetRefusal(environment.EnvironmentName, getVariable, developmentAuthentication);

        if (refusal is not null)
        {
            throw new InvalidOperationException(refusal);
        }
    }

    // Null when the build may start; otherwise one line that names the environment or the hosting variable, never a value
    // of a variable. It starts with DEV_AUTH, so the host log can be searched for it.
    internal static string? GetRefusal(string? environmentName, Func<string, string?> getVariable, bool developmentAuthentication)
    {
        if (!developmentAuthentication)
        {
            return null;
        }

        // IsDevelopment() of the host environment: the name, ignoring case.
        if (!string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            return $"DEV_AUTH: this Debug build of the Storyteller API skips the checks of users, so it starts only in the {Environments.Development} environment, and the host environment is '{environmentName}'. {Advice}";
        }

        var hosting = HostingVariables.FirstOrDefault(name => !string.IsNullOrEmpty(getVariable(name)));

        return hosting is null
            ? null
            : $"DEV_AUTH: this Debug build of the Storyteller API skips the checks of users, so it never starts on a hosting platform, and {hosting} is set. {Advice}";
    }
}
