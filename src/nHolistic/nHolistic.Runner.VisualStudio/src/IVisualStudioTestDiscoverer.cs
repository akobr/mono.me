using Microsoft.Extensions.Hosting;

namespace _42.nHolistic.Runner.VisualStudio;

public interface IVisualStudioTestDiscoverer : IHostedService
{
    bool IsSecondaryService { get; set; }

    Task? DiscoveringProcess { get; }
}
