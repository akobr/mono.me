using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace c0ded0c.Core.Processing
{
    public class Pipeline<TData> : IPipeline<TData>
    {
        private readonly LinkedList<IPipe<TData>> pipeline;

        public Pipeline(ILogger logger)
        {
            Logger = logger;
            pipeline = new LinkedList<IPipe<TData>>();
        }

        protected ILogger Logger { get; }

        public void AddPipe(IPipe<TData> pipe)
        {
            if (pipe == null)
            {
                throw new ArgumentNullException(nameof(pipe));
            }

            pipeline.AddLast(pipe);
        }

        public bool AddPipeBefore(IPipe<TData> pipeToAdd, IPipe<TData> pipeReference)
        {
            if (pipeToAdd == null)
            {
                throw new ArgumentNullException(nameof(pipeToAdd));
            }

            if (pipeReference == null)
            {
                throw new ArgumentNullException(nameof(pipeReference));
            }

            var unitNode = pipeline.Find(pipeReference);

            if (unitNode == null)
            {
                return false;
            }

            pipeline.AddBefore(unitNode, pipeToAdd);
            return true;
        }

        public async Task<TData> ProcessAsync(TData data)
        {
            return await OnPipelineProcessed(await ProcessUnitAsync(pipeline.First, data));
        }

        protected virtual void OnProcessingPipeNode(LinkedListNode<IPipe<TData>> pipeNode, TData data)
        {
            // no operation ( template method )
        }

        protected virtual Task<TData> OnPipelineProcessed(TData data)
        {
            // template method
            return Task.FromResult(data);
        }

        private Task<TData> ProcessUnitAsync(LinkedListNode<IPipe<TData>> pipeNode, TData data)
        {
            if (pipeNode == null)
            {
                return Task.FromResult(data);
            }

            OnProcessingPipeNode(pipeNode, data);

            if (pipeNode.Value is IPipeWithControl<TData> controlPipe)
            {
                return ProcessControlUnitAsync(pipeNode, controlPipe, data);
            }

            return ProcessSimpleUnitAsync(pipeNode, data);
        }

        private async Task<TData> ProcessSimpleUnitAsync(LinkedListNode<IPipe<TData>> pipeNode, TData data)
        {
            try
            {
                data = await pipeNode.Value.ProcessAsync(data);
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, $"Error during processing pipeline of '{typeof(TData).Name}' in unit '{pipeNode.Value.GetType().Name}'.");
            }

            return await ProcessUnitAsync(pipeNode.Next, data);
        }

        private Task<TData> ProcessControlUnitAsync(
            LinkedListNode<IPipe<TData>> pipeNode,
            IPipeWithControl<TData> controlPipe,
            TData data)
        {
            try
            {
                return controlPipe.ProcessAsync(data, (resultData) => ProcessUnitAsync(pipeNode.Next, resultData));
            }
            catch (Exception exception)
            {
                Logger.LogError(
                    exception,
                    $"Error during processing pipeline of '{typeof(TData).Name}' in unit '{pipeNode.Value.GetType().Name}'. The unit with control of the flow will be skipped.");

                // Skip of the unit
                return ProcessUnitAsync(pipeNode.Next, data);
            }
        }
    }
}
