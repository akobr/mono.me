using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "tests";

    public string ContentRootPath { get; set; } = ".";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
