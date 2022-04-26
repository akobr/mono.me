using System;
using System.Threading.Tasks;

namespace c0ded0c.Core.Processing
{
    public abstract class PipeWithControl<TData> : IPipeWithControl<TData>
    {
        public abstract Task<TData> ProcessAsync(TData data);

        public virtual async Task<TData> ProcessAsync(TData data, Func<TData, Task<TData>> nextFlow)
        {
            TData transformedData = await ProcessAsync(data);

            if (nextFlow == null)
            {
                return transformedData;
            }

            return await nextFlow(transformedData);
        }
    }
}
