using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Monorepo.Cli
{
    public class AsyncLazy<T>
    {
        private readonly object locker = new();
        private readonly Func<CancellationToken, Task<T>> factory;
        private Task<T>? task;

        public AsyncLazy(T value)
        {
            task = System.Threading.Tasks.Task.FromResult(value);
            factory = (_) => task;
        }

        public AsyncLazy(Task<T> task)
        {
            this.task = task;
            factory = (_) => task;
        }

        public AsyncLazy(Func<Task<T>> factory)
        {
            this.factory = (_) => factory();
        }

        public AsyncLazy(Func<CancellationToken, Task<T>> factory)
        {
            this.factory = factory;
        }

        public Task<T> Task => GetValueAsync();

        public async Task<T> GetValueAsync(CancellationToken cancellationToken = default, TaskCreationOptions options = TaskCreationOptions.None, TaskScheduler? scheduler = null)
        {
            if (task is not null)
            {
                return await task;
            }

            lock (locker)
            {
                task ??= System.Threading.Tasks.Task.Factory.StartNew(
                            () => factory(cancellationToken),
                            cancellationToken,
                            options,
                            scheduler ?? TaskScheduler.Current)
                         .Unwrap();
            }

            return await task;
        }

        public TaskAwaiter<T> GetAwaiter()
        {
            return Task.GetAwaiter();
        }

        public ConfiguredTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext)
        {
            return Task.ConfigureAwait(continueOnCapturedContext);
        }
    }
}
