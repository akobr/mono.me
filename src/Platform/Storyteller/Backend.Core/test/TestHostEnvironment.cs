using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string environmentName = "Production")
    {
        EnvironmentName = environmentName;
    }

    public string EnvironmentName { get; set; }

    public string ApplicationName { get; set; } = "tests";

    public string ContentRootPath { get; set; } = ".";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
