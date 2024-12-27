using System;
using System.Collections.Generic;
using System.Text;
using MediatR;
using QuikGraph;
using QuikGraph.Algorithms.ConnectedComponents;
using QuikGraph.Graphviz;

namespace _42.nHolistic;

public class ExecutionContextBuilder(
    ITestCasesMapper mapper,
    IResultAttachmentsService attachments,
    IPublisher publisher)
    : IExecutionContextBuilder
{
    private readonly AdjacencyGraph<TestCaseContext, Edge<TestCaseContext>> dependencyGraph = new();
    private readonly AdjacencyGraph<TestCaseContext, UndirectedEdge<TestCaseContext>> parallelismGraph = new();
    private readonly HashSet<TestCaseContext> nonParallelizable = new();

    public void RegisterTestCase(TestCaseContext testCase)
    {
        dependencyGraph.AddVertex(testCase);

        foreach (var dependency in testCase.Case.Dependencies)
        {
            var dependencies = mapper.GetTestCases(dependency);
            dependencyGraph.AddVerticesAndEdgeRange(
                dependencies
                    .Where(dependent => !ReferenceEquals(testCase, dependent))
                    .Select(dependent => new Edge<TestCaseContext>(testCase, dependent)));
        }

        if (testCase.Attribute.NonParallelizable)
        {
            nonParallelizable.Add(testCase);
            return;
        }

        parallelismGraph.AddVertex(testCase);
        var nonParallelizableWith = testCase.Attribute.NonParallelizableWith ?? [];

        foreach (var parallelism in nonParallelizableWith)
        {
            var parallelisms = mapper.GetTestCases(parallelism);
            parallelismGraph.AddVerticesAndEdgeRange(
            parallelisms
                    .Where(parallel => !ReferenceEquals(testCase, parallel))
                    .Select(parallel => new UndirectedEdge<TestCaseContext>(testCase, parallel)));
        }
    }

    public async Task<ExecutionContext> BuildAsync()
    {
        var filePaths = new List<string>();

        if (!dependencyGraph.IsEdgesEmpty)
        {
            var filePathDependencies = await CreateAttachmentDependencyGraphAsync();
            filePaths.Add(filePathDependencies);
        }

        if (!parallelismGraph.IsEdgesEmpty)
        {
            var filePathParallelism = await CreateAttachmentParallelismGraphAsync();
            filePaths.Add(filePathParallelism);
        }

        if (nonParallelizable.Count > 0)
        {
            var filePathNonParallelizable = await attachments.CreateJsonAttachmentAsync(
                nonParallelizable.Select(testCase => testCase.Case.FullyQualifiedName),
                "runner/non-parallelizable.json");
            filePaths.Add(filePathNonParallelizable);
        }

        if (filePaths.Count > 0)
        {
            await publisher.Publish(new ReportAttachmentsNotification { Attachments = filePaths });
        }

        if (!dependencyGraph.IsEdgesEmpty)
        {
            var cycles = dependencyGraph.FindCycles();

            if (cycles.Count > 0)
            {
                var cyclesText = string.Join(
                    "; ",
                    cycles.Select(cycle => string.Join(
                        " -> ",
                        cycle.Select(tCase => tCase.Case.FullyQualifiedName))));

                throw new InvalidOperationException(
                    $"Cycle(s) detected in the test case dependencies: {cyclesText}");
            }
        }

        var random = new Random();
        var context = new ExecutionContext();
        var batchMap = new Dictionary<Guid, ExecutionBatch>();
        var testCasesMap = new Dictionary<TestCaseContext, LinkedListNode<TestCaseContext>>();

        // TODO: [P2] the dependencies of non-parallelizable tests could run in parallel in previous stage(s),
        // but the first implementation is naive and everything runs in sequence
        // = if a test case in non-parallelizable, all its dependencies are also non-parallelizable
        if (nonParallelizable.Count > 0)
        {
            var synchronousBatch = PrepareBatch(
                nonParallelizable.OrderBy(_ => random.Next()),
                new ExecutionBatchCreationBehaviour
                {
                    IsNonParallelizable = true,
                    TestCasesMap = testCasesMap,
                });

            batchMap[synchronousBatch.Id] = synchronousBatch;
            context.Batches.Add(synchronousBatch);
            context.Stages.Add(new ExecutionStage
            {
                Batches = [synchronousBatch],
            });
        }

        // TODO: [P1] this should be configurable
        var maxParallelismLevel = Math.Max(Environment.ProcessorCount - 1, 2);

        // get components from the parallelism graph (used for building batches)
        var algorithm = new WeaklyConnectedComponentsAlgorithm<TestCaseContext, UndirectedEdge<TestCaseContext>>(parallelismGraph);
        algorithm.Compute();

        var dependencyGraphOfBatches = new AdjacencyGraph<ExecutionBatch, Edge<ExecutionBatch>>();
        var smallComponents = new List<BidirectionalGraph<TestCaseContext, UndirectedEdge<TestCaseContext>>>();

        foreach (var component in algorithm.Graphs)
        {
            if (component.VertexCount <= 3)
            {
                smallComponents.Add(component);
                continue;
            }

            var implicitBatch = PrepareBatch(
                component.Vertices.OrderBy(_ => random.Next()),
                new ExecutionBatchCreationBehaviour { TestCasesMap = testCasesMap });
            batchMap[implicitBatch.Id] = implicitBatch;

            dependencyGraphOfBatches.AddVertex(implicitBatch);
            dependencyGraphOfBatches.AddVerticesAndEdgeRange(
                implicitBatch.Dependencies
                    .Select(depId => new Edge<ExecutionBatch>(implicitBatch, batchMap[depId])));
        }

        foreach (var components in smallComponents.Chunk(8))
        {
            var explicitBatch = PrepareBatch(
                components
                    .SelectMany(component => component.Vertices)
                    .OrderBy(_ => random.Next()),
                new ExecutionBatchCreationBehaviour { TestCasesMap = testCasesMap });
            batchMap[explicitBatch.Id] = explicitBatch;

            dependencyGraphOfBatches.AddVertex(explicitBatch);
            dependencyGraphOfBatches.AddVerticesAndEdgeRange(
                explicitBatch.Dependencies
                    .Select(depId => new Edge<ExecutionBatch>(explicitBatch, batchMap[depId])));
        }

        // get components from the parallelism graph (used for building batches)
        var algorithmBatches = new WeaklyConnectedComponentsAlgorithm<ExecutionBatch, Edge<ExecutionBatch>>(dependencyGraphOfBatches);
        algorithmBatches.Compute();
        var parallelStages = new LinkedList<ExecutionStage>();
        parallelStages.AddLast(new ExecutionStage());

        foreach (var component in algorithmBatches.Graphs.OrderByDescending(graph => graph.VertexCount))
        {
            var startBatch = component.GetRootVertex()!;
            var batchQueue = new Queue<(ExecutionBatch Batch, LinkedListNode<ExecutionStage>? LastNode)>();
            batchQueue.Enqueue((startBatch, null));

            while (batchQueue.Count > 0)
            {
                var pair = batchQueue.Dequeue();
                var stageNode = pair.LastNode is null
                    ? parallelStages.Last!
                    : pair.LastNode.Previous ?? parallelStages.AddBefore(pair.LastNode, new ExecutionStage());

                stageNode = GetAvailableStage(stageNode, pair.Batch, maxParallelismLevel);
                stageNode.Value.Batches.Add(pair.Batch);

                if (component.TryGetOutEdges(pair.Batch, out var edges))
                {
                    foreach (var nextBatch in edges.Select(edge => edge.Target))
                    {
                        batchQueue.Enqueue((nextBatch, stageNode));
                    }
                }
            }
        }

        context.Stages.AddRange(parallelStages);
        return context;
    }

    private LinkedListNode<ExecutionStage> GetAvailableStage(LinkedListNode<ExecutionStage> stageNode, ExecutionBatch batch,  int maxParallelismLevel)
    {
        do
        {
            if (stageNode.Value.Batches.Any(stagedBatch => batch.Dependencies.Contains(stagedBatch.Id)))
            {
                stageNode = stageNode.List!.AddAfter(stageNode, new ExecutionStage());
                return stageNode;
            }

            if (stageNode.Value.Batches.Count >= maxParallelismLevel
                || (batch.IsNonParallelizable && stageNode.Value.Batches.Count > 0)
                || stageNode.Value.Batches.Any(stagedBatch => stagedBatch.IsNonParallelizable
                                                             || batch.NonParallelizableWith.Contains(stagedBatch.Id)))
            {
                stageNode = stageNode.Previous ?? stageNode.List!.AddFirst(new ExecutionStage());
                continue;
            }

            return stageNode;
        }
        while (true);
    }

    private ExecutionBatch PrepareBatch(IEnumerable<TestCaseContext> testCases, ExecutionBatchCreationBehaviour? behaviour = null)
    {
        behaviour ??= new ExecutionBatchCreationBehaviour();
        var batchId = Guid.NewGuid();
        var cases = new LinkedList<TestCaseContext>();
        var map = behaviour.TestCasesMap;
        var queue = new Queue<LinkedListNode<TestCaseContext>>();
        var nonParallelizable = new HashSet<Guid>();
        var dependencies = new HashSet<Guid>();

        foreach (var testCase in testCases)
        {
            if (testCase.State >= TestCaseExecutionState.LineUp)
            {
                if (behaviour.TreatPlacedTestCaseAsNonParallelizable)
                {
                    nonParallelizable.Add(testCase.BatchId!.Value);
                }

                continue;
            }

            var node = cases.AddLast(testCase);
            map[testCase] = node;
            queue.Enqueue(node);
        }

        // reorder and add all dependencies based on the dependency graph
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            var testCase = node.Value;

            // process all direct dependencies and queue them up
            var edges = dependencyGraph.OutEdges(testCase);
            foreach (var edge in edges)
            {
                var dependency = edge.Target;

                if (dependency.State >= TestCaseExecutionState.LineUp)
                {
                    var dependentBatchId = dependency.BatchId!.Value;
                    dependencies.Add(dependentBatchId);
                    continue;
                }

                var isPlaced = map.TryGetValue(dependency, out var dependencyNode);
                if (isPlaced)
                {
                    if (!ReferenceEquals(dependencyNode!.List, cases))
                    {
                        var dependentBatchId = dependency.BatchId!.Value;
                        dependencies.Add(dependentBatchId);
                        continue;
                    }

                    // if the dependency is already placed, check if it is placed before the current node
                    var isCorrectlyPlaced = dependencyNode!.IsBefore(node);
                    if (!isCorrectlyPlaced)
                    {
                        cases.Remove(dependencyNode!);
                        cases.AddBefore(node, dependencyNode!);
                    }

                    continue;
                }

                // if the dependency is not placed yet, place it before the current node and queue it up
                dependencyNode = cases.AddBefore(node, dependency);
                map[dependency] = dependencyNode;
                queue.Enqueue(dependencyNode);
            }

            // mark the test case as ready to run
            testCase.BatchId = batchId;
            testCase.State = TestCaseExecutionState.LineUp;
        }

        var batch = new ExecutionBatch
        {
            Id = batchId,
            TestCases = cases,
            Dependencies = dependencies,
            NonParallelizableWith = nonParallelizable,
            IsNonParallelizable = behaviour.IsNonParallelizable,
        };

        return batch;
    }

    private Task<string> CreateAttachmentDependencyGraphAsync()
    {
        var dot = dependencyGraph.ToGraphviz();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dot));
        return attachments.CreateAttachmentAsync(stream, "runner/dependency-graph.dot");
    }

    private Task<string> CreateAttachmentParallelismGraphAsync()
    {
        var dot = parallelismGraph.ToGraphviz();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dot));
        return attachments.CreateAttachmentAsync(stream, "runner/parallelism-graph.dot");
    }

    private class ExecutionBatchCreationBehaviour
    {
        public bool IsNonParallelizable { get; init; }

        // TODO: [P2] not implemented yet
        public bool PlaceDependenciesIntoNewBatch { get; init; }

        public bool TreatPlacedTestCaseAsNonParallelizable { get; init; } = true;

        public Dictionary<TestCaseContext, LinkedListNode<TestCaseContext>> TestCasesMap { get; init; } = new();
    }
}
