# Overview of 2S platform

::: danger
The platform is under development. See [the road map](road-map).
:::

The main concepts of the **2S** platform are described here. 

## Storyteller

The hearth of the platform. The term ecosystem is vital in the concept of a managable modulith. It is a description and a model of your product. The description can be used as a data source for fundamental questions, like *what is used by a concrete customer?* The storyteller describes the entire ecosystem in simple but flexible way of annotations and acts as a single point of truth for configuring a satellite in any state/context.

The model of annotations defines all critical relationships from the business point of view. The shape of it is crucial and probably the most important decision to make. The concept of annotation is quite abstract, flexible, and still simple. There are these annotations:

:::tabs
== default
``` mermaid
flowchart LR
  subject([subject])
  context([context])
  responsibility([responsibility])
```
== simple
``` mermaid
flowchart LR
  subject([subject])
  responsibility([responsibility])
```
== extend
``` mermaid
flowchart LR
  subject([subject])
  context([context])
  responsibility([responsibility])
  unit([unit])
```
:::

The **subject** should represent the most critical entity, like the sun of your business solar system. The next is **context**, representing a parallel universe in the business solar system. You can imagine it as a first level of abstraction. The last annotation of **responsibility** is like a planet or a building block of your business. In most cases, a responsibility represents one satellite in the modulith application.

The relationships between these blocks are illustrated in the next diagram. A subject has one or more contexts and **uses** multiple responsibilities. A combination of context and responsibility is a concrete unit of work or **execution** of a satellite.

:::tabs
== default
``` mermaid
flowchart LR
  subject([subject])
  context([context])
  responsibility([responsibility])
  usage([usage])
  execution([execution])

  style usage stroke-dasharray: 5 5
  style execution stroke-dasharray: 5 5

  subject -- 1..N --> context
  subject -- 1..N --> usage
  responsibility -- 1..N --> usage
  usage -- 1..N --> execution
  context -- 1..N --> execution
```
== simple
``` mermaid
flowchart LR
  subject([subject])
  responsibility([responsibility])
  usage([usage])
  execution([execution])

  style usage stroke-dasharray: 5 5
  style execution stroke-dasharray: 5 5

  subject -- 1..N --> usage
  responsibility -- 1..N --> usage
  usage -- 1..1 --> execution
```
== extend
``` mermaid
flowchart LR
  subject([subject])
  context([context])
  responsibility([responsibility])
  unit([unit])
  usage([usage])
  execution([execution])
  scheduled-unit([unit of execution])

  style usage stroke-dasharray: 5 5
  style execution stroke-dasharray: 5 5
  style scheduled-unit stroke-dasharray: 5 5

  subject -- 1..N --> context
  subject -- 1..N --> usage
  responsibility -- 1..N --> usage
  responsibility -- 1..N --> unit
  usage -- 1..N --> execution
  context -- 1..N --> execution
  execution -- 1..N --> scheduled-unit
  unit -- 1..N --> scheduled-unit
```
:::

The model is kept simple but generic to make it possible to describe a lot of variations of complex software systems. It can describe the relationship between your SaS product and your customers, complex business workflows, or differences between major software versions released yearly. There are no limits to the imagination and what you want to describe by the annotation model. It should always be something that will help you to manage your product.

::: tip
This platform can be very useful for the microservices approach because it can help you picture the system's whole architecture and simplify management. The platform is modeled to handle hundreds of thousands of annotations and serve them blazingly fast. 
:::

## Supervisor

A service where the entire ecosystem can be reviewed in real-time and notifications based on variable rules can be sent outside the boundaries of the ecosystem. It acts as a watcher for the health of each satellite and shows what is currently running. It supports the self-registration concept and auto-discovery.

You can connect to the supervisor as a stream source to see the real-time events from your ecosystem. There is a secured web UI or CLI client. The events can be overwatched based on variable rules, and variable actions can be triggered, e.g., a notification will be sent or a new support ticket created.

The supervisor comes with a simple SDK library, and it's implemented on top of standardized concepts in .net; it uses [health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) or can retrieve messages through *ILogger*.

::: tip
Try AI to detect anomalies in your ecosystem. <Badge type="warning" text="under the development" />
:::

## Scheduler

The last actor for managing time-based and recurrent jobs. It can be handy when you have many of them and want to describe smaller units than just responsibilities (satellites) in the ecosystem. Imagine how convenient it is to know: *what and when is running per each customer?*
