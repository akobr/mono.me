# Introduction to crumble <Badge type="danger" text="60% done" />

Crumble is an elegant abstraction for even-driven jobs. It **helps you define event-driven or time-based jobs that are simple to use** in your distributed system, as serverless functions, or as background workers in a monolith application. The abstraction allows a simple swap at any time, and it natively integrates with the [2S platform](/platform/introduction).

::: tip
It is based on source generators and uses no reflection, which makes it extremely fast and robust. It is going to be an abstraction for [Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-overview), [TickerQ](https://tickerq.net/), and [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans/). *The current focus is to polish the solution for Orleans.*
:::

The main idea is to **bring together a proper event-driven approach, serverless principles, and your current application architecture**. Abstract it from a concrete framework or cloud platform, and make it easily accessible from any place of .net platform, as a standalone application, distributed system, or Azure native serverless compute services, the Azure Functions.

## Getting started

1. Create a project for your jobs.
2. Add a reference to the desired source generator.

```powershell
dotnet add package 42.Crumble.SourceGenerator.<architecture>
```

3. Implement jobs.
   
```csharp
[Crumb, TimeEvent("42 */1 * * *")]
public async Task DoWork()
{
    await Task.Delay(1000);
}
```
5. Reference the crumble runtime with your application.

```powershell
dotnet add package 42.Crumble.Runtime.<architecture>
```

6. Reference and register jobs with your application.

```csharp
public void EntryPoint(IHostApplicationBuilder builder)
{
    builder.Services.AddCrumble().AddCrumbs();
}
```
