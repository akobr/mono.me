// Next to IHttpClientBuilder and IHealthChecksBuilder, so the builder of services.AddStoryteller(…) needs no using.
namespace Microsoft.Extensions.DependencyInjection;

// The services of Storyteller Hosting, for the calls that add to them.
public interface IStorytellerHostingBuilder
{
    IServiceCollection Services { get; }
}
