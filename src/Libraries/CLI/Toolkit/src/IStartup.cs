using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.CLI.Toolkit;

public interface IStartup
{
    void ConfigureApplication(IConfigurationBuilder builder);

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
