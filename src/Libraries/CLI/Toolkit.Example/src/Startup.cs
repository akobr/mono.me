using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.CLI.Toolkit.Example;

public class Startup : IStartup
{
    public void ConfigureApplication(IConfigurationBuilder builder)
    {
        // add any configuration here
        // builder.AddJsonFile("appsettings.json", false, false);
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddCliToolkit(); // register the toolkit (IExtendedConsole, IRenderer, IPrompter, IProgressReporter)
        // register your services here
    }
}
