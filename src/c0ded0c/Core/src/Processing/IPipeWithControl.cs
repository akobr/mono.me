using System;
using System.Threading.Tasks;

namespace c0ded0c.Core.Processing
{
    public interface IPipeWithControl<TData> : IPipe<TData>
    {
        Task<TData> ProcessAsync(TData data, Func<TData, Task<TData>> nextFlow);
    }
}
