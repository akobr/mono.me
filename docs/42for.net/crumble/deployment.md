# How to deploy and do infrastructure

There are many ways to run and host your event-driven jobs. It depends on whether you want to make it part of an existing .NET application or build it as an independent module in your ecosystem.

## Run it as part of a .NET application

There is a simple solution if you want to run it as part of your .NET application. For this reason, there is a variant that generates crumbs as background jobs part of [TickerQ](https://tickerq.net/). A good solution for a smaller application where it is fine for everything to run in the same place.

```powershell
# Source generator
dotnet add package 42.Crumble.SourceGenerator.TickerQ
# Runtime
dotnet add package 42.Crumble.Runtime.TickerQ
```

## Native serverless approach in Azure

Another attractive option is to run serverless Azure Functions and trigger the jobs there. This is definitely a great option and can be good for your wallet, as well.

```powershell
# Source generator
dotnet add package 42.Crumble.SourceGenerator.AzureFunctionsWorker
# Runtime
dotnet add package 42.Crumble.Runtime.AzureFunctionsWorker
```

::: warning
Even though the Azure serverless platform is interesting, there are many drawbacks. It is difficult to split deliverables, handle service plans, and scale properly. At the same time, there are many technical limitations, and some basic features are treated as premium, e.g., a static IP address.
:::

## Pure distributed system with Orleans

The most advanced and flexible solution is to run a cluster. That is what I recommend, to manage a centralized Orleans framework cluster where you can place all your event-driven jobs across all your products, with simple, infinite scaling.

```powershell
# Source generator
dotnet add package 42.Crumble.SourceGenerator.Orleans
# Runtime
dotnet add package 42.Crumble.Runtime.Orleans
```