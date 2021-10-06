using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Monorepo.Cli
{
    public interface IStartup
    {
        void ConfigureApplication(IConfigurationBuilder builder);

        void ConfigureServices(IServiceCollection services);

        void ConfigureOptions(IConfiguration configuration, IServiceCollection services);
    }
}
