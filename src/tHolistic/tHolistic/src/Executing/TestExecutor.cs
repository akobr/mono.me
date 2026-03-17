using System.Diagnostics;
using System.Reflection;
using MediatR;

namespace _42.tHolistic;

public class TestExecutor(
    ITestCasesMapper testCasesMapper,
    IExecutionContextBuilder contextBuilder,
    IFixturesProcessingService fixtures,
    ISynchronizationService synchronization,
    ITestRunScopeFactory scopeFactory,
    ITypeActivator activator,
    IPublisher publisher)
    : ITestExecutor
{
    public async Task ExecuteTestCasesAsync(IEnumerable<TestCase> testCases, CancellationToken cancellationToken = default)
    {
        // TODO: [P3] don't ToList the test cases, just iterate over them
        var testCaseList = testCases.Select(testCase => testCase.CreateContext()).ToList();
        await publisher.Publish(
            new LogNotification { Level = LogMessageLevel.Informational, Message = $"tHolistic executor started with {testCaseList.Count} test case(s)." },
            cancellationToken);

        var context = await PrepareForExecutionAsync(testCaseList);
        await ExecuteInProcessAsync(context, cancellationToken);
    }

    private async Task ExecuteInProcessAsync(ExecutionContext context, CancellationToken cancellationToken)
    {
        // Prepare one task per each batch stage after state (stages are sequential, but batches within a stage are parallel)
        foreach (var stage in context.Stages)
        {
#if DEBUG
            foreach (var batch in stage.Batches)
            {
                await ExecuteBatchAsync(batch, cancellationToken);
            }
#else
            var tasks = new List<Task>();
            tasks.AddRange(stage.Batches.Select(batch => Task.Run(() => ExecuteBatchAsync(batch, cancellationToken), cancellationToken)));
            await Task.WhenAll(tasks);
#endif

            synchronization.CleanUp();
        }
    }

    private async Task<ExecutionContext> PrepareForExecutionAsync(List<TestCaseContext> testCaseList)
    {
        // sort tests by full qualified name, to make dependency tree and other relations deterministic and idempotent
        testCaseList.Sort();

        foreach (var testCase in testCaseList)
        {
            testCasesMapper.RegisterTestCase(testCase);
        }

        foreach (var testCase in testCaseList)
        {
            contextBuilder.RegisterTestCase(testCase);
        }

        var context = await contextBuilder.BuildAsync();
        return context;
    }

    private async Task ExecuteBatchAsync(ExecutionBatch batch, CancellationToken cancellationToken)
    {
        foreach (var testCase in batch.TestCases)
        {
            try
            {
                await ExecuteTestCaseAsync(testCase, cancellationToken);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }

    private async Task ExecuteTestCaseAsync(TestCaseContext testCase, CancellationToken cancellationToken)
    {
        var timeStamp = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var outcome = TestRunOutcome.None;
        using var servicesScope = scopeFactory.CreateScope();

        try
        {
            testCase.State = TestCaseExecutionState.Executing;
            await publisher.Publish(new ReportTestRunStartedNotification { TestCase = testCase.Case }, cancellationToken);

            // TODO: [P1] should this be part of a test execution?
            fixtures.PrepareFixtures(testCase.TargetType);
            var testClassInstance = activator.Activate(testCase.TargetType, testCase.Case);

            if (testCase.TargetMethod is not null)
            {
                var method = testCase.TargetMethod;
                var synchronizedAttribute = method.GetCustomAttribute<SynchronizedAttribute>();

                if (synchronizedAttribute is not null)
                {
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    if (synchronizedAttribute.SynchronizationKey is not null)
                    {
                        // TODO: [P1] migrate to SemaphoreSlim (possibly to Semaphore when used in multiple processes)
                        lock (synchronization.GetOrCreateLock(synchronizedAttribute.SynchronizationKey))
                        {
                            outcome = InvokeTestMethodAsync(method, testClassInstance, testCase, cancellationToken).Result;
                        }
                    }
                    else
                    {
                        await publisher.Publish(
                            new LogNotification
                            {
                                Level = LogMessageLevel.Warning,
                                Message = $"Test method '{testCase.Case.FullyQualifiedName}' has Synchronized attribute, but no synchronization key is provided. Test will be executed without synchronization.",
                            },
                            cancellationToken);
                    }
                }

                outcome = await InvokeTestMethodAsync(method, testClassInstance, testCase, cancellationToken);
            }
            else
            {
                // TODO: [P2] class test
                outcome = TestRunOutcome.Failed;
            }
        }
        catch (Exception exception)
        {
            await publisher.Publish(
                new LogNotification { Level = LogMessageLevel.Error, Message = exception.Message, },
                cancellationToken);

            // TODO: [P1] report error into test result
            outcome = TestRunOutcome.Failed;
        }
        finally
        {
            await publisher.Publish(new ReportTestRunEndedNotification { TestCase = testCase.Case, TestOutcome = outcome }, cancellationToken);
            testCase.State = TestCaseExecutionState.Done;
        }
    }

    private async Task<TestRunOutcome> InvokeTestMethodAsync(
        MethodInfo method,
        object testClassInstance,
        TestCaseContext testCase,
        CancellationToken cancellationToken)
    {
        var parameters = activator.ResolveParameters(method.GetParameters(), testCase.Case);
        var result = method.Invoke(testClassInstance, parameters);

        if (result is null)
        {
            return TestRunOutcome.Passed;
        }

        cancellationToken.ThrowIfCancellationRequested();

        switch (result)
        {
            case Task task:
                await task;
                return TestRunOutcome.Passed;

            case ValueTask valueTask:
                await valueTask;
                return TestRunOutcome.Passed;

            default:
                return TestRunOutcome.Passed;
        }
    }
}
