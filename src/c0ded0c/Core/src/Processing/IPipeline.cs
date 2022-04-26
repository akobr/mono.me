namespace c0ded0c.Core.Processing
{
    public interface IPipeline<TData> : IPipe<TData>
    {
        void AddPipe(IPipe<TData> pipe);

        bool AddPipeBefore(IPipe<TData> pipeToAdd, IPipe<TData> pipeReference);
    }
}
