using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.AspireHosting.UnitTests;

// An AppHost builder without DCP: ExecutionConfigurationBuilder evaluates the environment of a resource as Aspire does
// at start, in run mode (parameters resolved from Parameters:{name}) or in publish mode (their manifest expressions).
internal sealed class TestAppHost
{
    // Nothing is read from it: projects excluded from their launch profile need no file.
    public static readonly string Directory = Path.Combine(Path.GetTempPath(), "storyteller-apphost");

    private TestAppHost(IDistributedApplicationBuilder builder)
    {
        Builder = builder;
    }

    public IDistributedApplicationBuilder Builder { get; }

    public static TestAppHost Run()
    {
        return new TestAppHost(DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            DisableDashboard = true,
            ProjectDirectory = Directory,
        }));
    }

    public static TestAppHost Publish()
    {
        return new TestAppHost(DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = ["--operation", "publish", "--publisher", "manifest", "--output-path", Path.Combine(Directory, "manifest")],
            DisableDashboard = true,
            ProjectDirectory = Directory,
        }));
    }

    public TestAppHost WithParameter(string name, string value)
    {
        Builder.Configuration[$"Parameters:{name}"] = value;
        return this;
    }

    public IResourceBuilder<ProjectResource> AddProject(string name = "worker")
    {
        return Builder.AddProject(name, Path.Combine(Directory, name, $"{name}.csproj"), options => options.ExcludeLaunchProfile = true);
    }

    public IResourceBuilder<ExecutableResource> AddExecutable(string name = "node-worker")
    {
        return Builder.AddExecutable(name, "node", Directory, "worker.js");
    }

    public IResourceBuilder<ContainerResource> AddContainer(string name = "python-worker")
    {
        return Builder.AddContainer(name, "python", "3.13");
    }

    // The variables that have a value. Every SDK reads an empty one as unset, and WithReference writes the names a
    // project or an executable would inherit from the AppHost empty in run mode; ReadAllVariablesAsync keeps them.
    public async Task<IReadOnlyDictionary<string, string?>> ReadVariablesAsync(IResource resource)
    {
        return Sorted((await ReadAllVariablesAsync(resource)).Where(variable => !string.IsNullOrEmpty(variable.Value)));
    }

    // The STORYTELLER_* and ConnectionStrings__* variables only, as Aspire adds OTEL_* and others; ordered by name, as
    // the order Aspire gathers them in is not the contract. Always bounded: a value that never resolves (an endpoint
    // that is not allocated without DCP) waits.
    public async Task<IReadOnlyDictionary<string, string?>> ReadAllVariablesAsync(IResource resource)
    {
        var result = await BuildAsync(resource);

        result.Exception.ShouldBeNull();
        return Sorted(result.EnvironmentVariables
            .Where(variable => variable.Key.StartsWith("STORYTELLER_", StringComparison.Ordinal) || variable.Key.StartsWith("ConnectionStrings__", StringComparison.Ordinal))
            .Select(variable => KeyValuePair.Create(variable.Key, (string?)variable.Value)));
    }

    public async Task<IExecutionConfigurationResult> BuildAsync(IResource resource)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        return await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(Builder.ExecutionContext, NullLogger.Instance, timeout.Token);
    }

    private static SortedDictionary<string, string?> Sorted(IEnumerable<KeyValuePair<string, string?>> variables)
    {
        return new SortedDictionary<string, string?>(variables.ToDictionary(StringComparer.Ordinal), StringComparer.Ordinal);
    }
}
