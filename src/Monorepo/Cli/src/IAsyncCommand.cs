using System.Threading.Tasks;

namespace _42.Monorepo.Cli
{
    public interface IAsyncCommand
    {
        Task OnExecuteAsync();
    }
}
