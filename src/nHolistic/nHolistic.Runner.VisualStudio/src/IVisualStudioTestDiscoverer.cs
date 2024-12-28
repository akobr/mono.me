using Microsoft.Extensions.Hosting;

namespace _42.tHolistic.Runner.VisualStudio;

public interface IVisualStudioTestDiscoverer : IHostedService
{
    bool IsSecondaryService { get; set; }

    Task? DiscoveringProcess { get; }
}
