# Quick start

Five steps from "never heard of it" to a .NET service whose options change while it runs. Put the kettle on; it will still be warm at the end.

**You need:** the .NET 10 SDK, and an account on a 2S deployment with an organization and a project. The [admin UI](../admin-ui#signing-in-and-the-first-project) walks you through both when you first sign in. The examples use the organization `house` and the project `main`; use your own names.

## 1. Install `sform` and sign in

```bash
dotnet tool install 42.Platform.Cli --global --prerelease
sform account                 # prints an address and a code: confirm them in your browser
sform account set -p house.main
```

The last line makes `house.main` the default project of every later command, so you don't have to type it again.

**You now have:** a terminal that talks to your project.

## 2. Describe one customer using one module

Northwind (a subject) uses invoicing (a responsibility) in production (a context). One record says all of that:

:::tabs
== sform
```bash
sform story set exe.northwind.invoicing.production
```
== admin UI
**Annotations** → **New**, pick **Execution**, and type `northwind`, `invoicing` and `production`.
:::

Creating an execution creates everything above it that's missing: the subject `sbt.northwind`, the responsibility `rst.invoicing`, the context `cnt.northwind.production` and the usage `usg.northwind.invoicing`. Five records for the price of one.

**You now have:** a catalog that knows who runs what, where.

## 3. Write each difference once

The product default goes on the responsibility, Northwind's own wish on the subject:

:::tabs
== sform
```bash
sform story config set rst.invoicing -x "invoicing.currency=EUR" -x "invoicing.retries=3"
sform story config set sbt.northwind -x "invoicing.currency=NOK"
sform story config exe.northwind.invoicing.production      # the merge, as the service will see it
```
== admin UI
**Configurations** → **New**, pick `rst.invoicing`, **Open editor**, **Create empty**, and save:

```json
{ "invoicing": { "currency": "EUR", "retries": 3 } }
```

Then the same for `sbt.northwind` with `{ "invoicing": { "currency": "NOK" } }`.
:::

The execution has no document of its own, and still reads `currency: NOK, retries: 3`. That's the merge doing its job.

**You now have:** one default and one exception, instead of two copies.

## 4. Create a machine for the service

Your service signs in as a **machine**, not as you. Restrict it to its own execution, so it can read its configuration and nothing else:

:::tabs
== sform
```bash
sform account machine create -a exe.northwind.invoicing.production
```
== admin UI
**Machines** → **New machine**, with `exe.northwind.invoicing.production` as its annotation key.
:::

It prints the variables the service needs, among them `STORYTELLER_ENDPOINT` and `STORYTELLER_API_KEY`. The key is shown **once**: keep it somewhere safe, like a password manager, not a sticky note.

**You now have:** credentials with exactly one job.

## 5. Read it from .NET

```bash
dotnet new web -n Invoicing && cd Invoicing
dotnet add package 42.Platform.Storyteller.Configuration --prerelease
```

`Program.cs`:

```csharp
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddStoryteller();               // reads STORYTELLER_* from the environment
builder.Services.AddStorytellerConfiguration();       // its logging and its refresher
builder.Services.Configure<InvoicingOptions>(builder.Configuration.GetSection("invoicing"));

var app = builder.Build();

app.MapGet("/", (IOptionsMonitor<InvoicingOptions> options) => options.CurrentValue);

app.Run();

public sealed class InvoicingOptions
{
    public string Currency { get; set; } = "";

    public int Retries { get; set; }
}
```

Run it with the machine's variables and the full key of the execution:

:::tabs
== bash
```bash
export STORYTELLER_ENDPOINT=https://api.2s.42for.net/api   # what machine create printed
export STORYTELLER_API_KEY=2s.…
export STORYTELLER_KEY=@house.main.default.exe.northwind.invoicing.production
dotnet run
```
== PowerShell
```powershell
$env:STORYTELLER_ENDPOINT = 'https://api.2s.42for.net/api'   # what machine create printed
$env:STORYTELLER_API_KEY = '2s.…'
$env:STORYTELLER_KEY = '@house.main.default.exe.northwind.invoicing.production'
dotnet run
```
:::

Open the address it prints: `{"currency":"NOK","retries":3}`. 🎉

## The party trick

Leave the service running and change your mind:

```bash
sform story config set sbt.northwind -x "invoicing.retries=7"
```

Within about 30 seconds the page says `"retries":7`, without a restart and without a deployment. And when someone asks, half a year from now, why Northwind has seven retries:

```bash
sform story config versions sbt.northwind
```

It has a name, a date and a version. Mystery solved, no archaeology needed.

## Where next

- Secrets in that document? [Key Vault secrets](key-vault).
- One worker for **every** customer instead of one service per customer? [Hosting](hosting).
- Tired of exporting variables by hand? [Aspire](aspire).
- Something says `401` or `403`? The [cheat sheet](cheat-sheet#when-things-go-sideways).
