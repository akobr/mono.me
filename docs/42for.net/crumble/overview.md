# Overview of crumble

To create an event-driven job, unit of work, function, or whatever you want to call it, is extremely simple. Just by marking a method with the `[Crumb]` attribute. This registers the crumb to crumble runtime, which can later be triggered by an event. However, if a specific trigger must be explicitly specified, there are two main options. First, an in-code approach using corresponding attributes `[TimeEvent]`, `[VolumeEvent]` or `[MessageEvent]`.

::: tip
Units of work (cumbs) and three events: time, data, and message.
:::

The runtime supports three triggers, no more are needed. `[TimeEvent]` based trigger that can follow a schedule defined by a CRON expression and be combined with a time zone. `[VolumeEvent]` represents a trigger of an event in a data source or file source. `[MessageEvent]` is an asynchronous event with or without a payload on the input or output side.

::: tip
The most flexible way is to connect your crumble orchestration to the [2S platform](/platform/introduction) and make the trigger fully configurable from within, without needing to change code or redeploy your application when a trigger is modified, added, or removed.
:::

## Simplest examples of crumbs

A time scheduled:

```csharp
[Crumb, TimeEvent("42 */2 * * *")]
public Task DoScheduledWork(DateTimeOffset startTime)
{
    // your business logic lies here
}
```

The concept of **volume** and its triggers serves as an abstraction of a data source. **A virtual source of entities.** It is optimized to work with a large number of entities, which can be structured in a tree-shaped folder structure. Sources, such as files, Azure storage, PostgreSQL entities, CosmosDB documents, and similar, can be connected to your crumbs.

```csharp
[Crumb, VolumeEvent("folder/entityName")]
public Task DoWorkOnEntity(string path)
{
    // your business logic lies here
}
```

Message triggers represent async communication on any of the supported channels. For example, Azure storage queues, message buses, EventHub, Kafka, and much more.

```csharp
[Crumb, MessageEvent]
public Task DoMessageWork(IMessageModel message)
{
    // your business logic lies here
}
```

When you put together your crumbs orchestration, you can take it into your application. It can be a serverless architecture built on Azure Applications, a pure distributed system powered by Orleans, or a monolithic application on the .NET platform. The only thing you have to do is register it with your `IServiceCollection` by calling `.AddCrumbs()` for every library with crumbs. The generation for different architectures can be achieved by referencing the corresponding source generator package.
