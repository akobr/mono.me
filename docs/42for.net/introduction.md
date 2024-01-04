# Introduction

My goal here is to help you to tame the beast of monolith without doing some crazy changes and introducing to many new things to your team. The main idea is: *"The simplicity it the way! Most important is the balance between flexibility and complexity."*.

::: info
For an already running product is imposible to do changes in a big bang way. You have to do your changes sustainably and slowly, have a solid plan and follow it for months/years.
:::

## How to do it?

By removing all compexities from your engineering processes you can make much fresh and hapier working environment. And you will realise that not too many specialists and resources are needed to run your show.

::: tip
Are you working on software product which are starting to having some technical issues because of monolith and legacy code? If yes, please continue reading and check what I'm offering here. If you are planing to build a totally new product, please stop thinking and start building your MVP as monolith, that will be always the best option for a totally new projects which need to be placed on market as fast as possible. If the second case is true, you can leave and start your work right now and come back later, after your MVP is done and prove!
:::

We should start with a simple question: *What are the exact problems in your product?* I'm assuming some maintanance complexities, scaling and performance problems and perhaps a mess in codebase + infrastructure.

You probably read over the internet a lot of buzzwords which should solve all your problems, e.g. microservices. Please be vigilant and don't use these principles as silver bullets. They can be really powerfull and usefull but only for cases where they are really needed and all thier benefits are used. Their complexity and learning curve are extremely steep and the final maintanance is expensive, that is why you shouldn't use it if it helps you to solve just some edge problems.

I recomend to **avoid**:

- [microservices](/architecture/no-microservices)
- complex infrastructure if not needed
- spliting you app into multiple independent blocks which are totally unrelated and not standartized
- hire external contractors to work on the core of your product
- any horizontal slices in your development, split frontend and backend, separate QA from developers, introduce separeted DevOps team, etc.
- split your codebase into multiple repositories, or doing any unnecesary fragmentation

I recomend **to do**:

- slowly disasemble your monolith to [modulith](/architecture/modulith) (modules + monolith)
- stay in [mono-repository](/monorepo/why-monorepo) and create standards for your small team of superheroes
- use [minimum infrastructure](/articles/infrastructure) as possible (serverless + event driven concepts)
  - use infrastructure as code, and make everything transparent and visible in your single repository
  - use containers with a simple abstraction, like [Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/overview)
- simplify processes around development (releasing, documentation, automated QA, code standards)
- learn to write clean and self-documenting code
- keep it simple!

**TODO: overview diagram + description**

## Where to start?

It is recomended to start with [the motivation article](/motivation).

Two open-source tools are offered to you. The first is a CLI interface with a lot of recomendations about how to manage [mono repository for .net platform](/monorepo/introduction). The second is a [modulith architecture](/architecture/modulith) and its [subscription-based platform](/platform/introduction).


