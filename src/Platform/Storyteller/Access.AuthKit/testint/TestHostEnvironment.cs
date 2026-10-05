using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Access.AuthKit.IntegrationTests;

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;

    public string ApplicationName { get; set; } = "integration-tests";

    public string ContentRootPath { get; set; } = ".";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
