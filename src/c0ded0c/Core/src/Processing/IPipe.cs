using System.Threading.Tasks;

namespace c0ded0c.Core.Processing
{
    public interface IPipe<TData>
    {
        Task<TData> ProcessAsync(TData data);
    }
}
