using System;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface ITool
    {
        Task BuildAsync(IProgress<IToolProgress>? observer = null, CancellationToken cancellation = default);
    }
}
