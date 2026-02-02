# Crumbs and how work with them

Crumbs are created by decorating a method with `[Crumb]` attribute. By default, a crumb is designed as a unit of work, with each execution totally isolated and unrelated to the others. Crumbs can be grouped logically by defining them in the same class. Each crumb is asynchronous by design, and they can have input and output.

## Crumb identification

Each crumb has a unique identifier that can be used for lossecoupling execution and be included in observability logs. The identification is calculated by default based on the full qualified name and parameter types. If you want to manage your crumb keys yourself, you can do it by `Key` property on the attribute. It is important to make sure the keys are globally unique.

```csharp
public class SimpleCrumbs
{
    [Crumb(Key = "SimpleCrumbs.CustomAndUniqueCrumbKey")]
    public Task SimpleJob() => Task.CompletedTask;
}
```

## Execution of a crumb

Executing a crumb can be done in two ways. The first option is a strongly typed approach, which will be preferred within the application and in directly referenced libraries. It is done by calling the generated `I<CrumbsClassName>Executor` interface. Each crumb is represented as a method on the interface.

```csharp
public interface IAsyncCrumbsExecutor
{
    Task SimpleJob();
}
```

The second way is more loosely coupled, where you need to know the key and use the generic interface `IFlowClient`. There are multiple methods to call a crumb with/without input and output. Another possibility is to fire-and-forget.

```csharp
public async Task Orchestrate(IFlowClient flow)
{
    var data = await flow.ExecuteCrumbAsync<List<InputData>>("InputCrumbKey");
    await flow.ExecuteCrumbAsync<List<InputData>, string>("ProcessingCrumbKey", data);
}
```

## Observability

A major focus is on observability and the potential for reverse-engineering. In event-driven systems, this is one of the most important aspects, not just to understand what is happening right now, but also to check the process that happened a couple of days ago. For these reasons, there is native integration with OpenTelemetry, everything is detailedly logged, and many metrics are reported. Everything around crumbs, their orchestration, and initial events.

When more is needed, it is possible to enable **a journal** that will report and store details of the processing, including all input and output models. This is extremely handy for reverse engineering and for replaying any job history. The journal can be stored in your favorite persistent storage. The only requirement is to reference or implement a desired provider.

## Syncronization of a crumb

There is a built-in option to ensure each crumb is a singleton, and that every call is processed synchronously. The behavior can be enabled by setting the `[Crumb]` attribute's `IsSingleAndSynchronized` property to `true`.

```csharp
[Crumb(IsSingleAndSynchronized = true)]
public void HelloWorld()
{
    Console.WriteLine("Hello, World!");
}
```
