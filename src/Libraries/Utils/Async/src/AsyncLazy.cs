using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace _42.Utils.Async;

public class AsyncLazy<T> : Lazy<Task<T>>
{
    public AsyncLazy(Func<T> valueFactory)
        : base(() => Task.Factory.StartNew(valueFactory, default, TaskCreationOptions.None, TaskScheduler.Current))
    {
        // no operation
    }

    public AsyncLazy(Func<Task<T>> taskFactory)
        : base(() => Task.Factory.StartNew(taskFactory, default, TaskCreationOptions.None, TaskScheduler.Current)
            .Unwrap())
    {
        // no operation
    }

    public TaskAwaiter<T> GetAwaiter()
    {
        return Value.GetAwaiter();
    }
}
