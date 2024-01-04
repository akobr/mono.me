using System.Threading.Tasks;

namespace _42.CLI.Toolkit;

public interface IAsyncCommand
{
    Task<int> OnExecuteAsync();
}
