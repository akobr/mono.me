# Introduction

My goal is to help you tame the beast of monolith without making some crazy changes and introducing many new things to your team. The main idea is: *"The simplicity is the way! Most important is the balance between flexibility and complexity."*.

::: info
It is impossible for an already running product to make changes in a big bang way. You must make your changes sustainably and slowly, have a solid plan, and follow it for months/years.
:::

## How to do it?

By removing all complexities from your engineering processes, you can create a much fresher and happier working environment. And you will realize that only a few specialists and resources are needed to run your show.

::: tip
Are you working on software products that are starting to have some technical issues because of monolith and legacy code? If yes, please continue reading and check what I'm offering here. If you plan to build a new product, please stop thinking and start building your MVP as a monolith. That will always be the best option for new projects that must be placed on the market quickly. If the second case is genuine, you can leave and start your work right now and return after your MVP is done and proven!
:::

We should start with a simple question: *What are the exact problems with your product?* I'm assuming some maintenance complexities, scaling and performance problems, and a mess in the codebase + infrastructure.

You can read many buzzwords over the internet which should solve all your problems, e.g., *microservices*. Please be vigilant and don't use these principles as silver bullets. They can be powerful and valuable, but only in cases where they are needed and all their benefits are used. They are complex, the learning curve is incredibly steep, and the final maintenance is expensive; that is why you shouldn't use them if they help you solve some edge problems.

I recomend to **avoid**:

- [microservices](/architecture/no-microservices)
- complex infrastructure if not needed
- splitting your app into multiple independent blocks that are unrelated and not standardized
- hire external contractors to work on the core of your product
- any horizontal slices in your development, split frontend and backend, separate QA from developers, introduce a separate DevOps team, etc.
- split your codebase into multiple repositories, or do any unnecessary fragmentation

I recommend **to do**:

- slowly disassemble your monolith to [modulith](/architecture/modulith) (modules + monolith)
- stay in [mono-repository](/monorepo/why-monorepo) and create standards for your small team of superheroes
- use [minimum infrastructure](/articles/infrastructure) as possible (serverless + event-driven concepts)
  - use infrastructure as code, and make everything transparent and visible in your single repository
  - use containers with a simple abstraction, like [Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/overview)
- simplify processes around development (releasing, documentation, automated QA, code standards)
- learn to write clean and self-documenting code
- keep it simple!

**TODO: overview diagram + description**

## Where to start?

It is recommended to start with [the motivation article](/motivation).

Two open-source tools are offered to you. The first is a CLI interface with many recommendations about managing [mono repository for .net platform](/mono repo/introduction). The second is a [modulith architecture](/architecture/modulith) with [subscription-based platform](/platform/introduction).


