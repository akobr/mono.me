using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Crumble;

public static class EntryPoint
{
    public static IServiceCollection AddTableJournal(this IServiceCollection @this)
    {
        @this.Replace(ServiceDescriptor.Singleton<IJournalClient, TablesJournalClient>());
        return @this;
    }
}
