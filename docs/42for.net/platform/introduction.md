# Introduction of 2S platform <Badge type="danger" text="35% done" />

::: danger
The platform is under development. See [the road map](road-map).
:::

The **2S platform**, as I call it, should help you to manage your products and complex monolith application(s). The **2S** represents **S**implicity and **S**ubscription-based platform.

::: info
I will use many expressions in the text that are described in the section about the [modulith](../architecture/modulith) architecture, for example, **ecosystem** and **satellite**. If you don't know them, I recommend reading that section first.
:::

The platform is used to describe the relationships and the building blocks in your ecosystem, plus it should help you manage it by intelligent monitoring, centralized configuration, and real-time event-based system logs. Everything is done by three independent actors, which can be used in combination or separately:

- Storyteller
- Supervisor
- Scheduler

The storyteller describes the entire business structure and dependencies in the ecosystem, and it acts as documentation and the subscription service for the runtime. The second part, the supervisor, is a clever and real-time system watcher. The last and most specific actor is a flexible scheduler of jobs or work units if a lot of time-based processing is needed and monitoring of them in a sustainable way is required.

::: tip
The platform is designed in a way that should help you understand the modeled ecosystem and act as documentation. That is extremely handy when you bring new faces to your team. Another superior concept is to speed up reverse engineering and debugging in case of an incident. As I mentioned many times **transparency and visibility** are some of the most fundamental properties of a healthy product.
:::

The actors are open-source, allowing you to deploy it on-premise, under your cloud account, or into any hybrid environment. If you need to bend some parts, please be your guest and create a PR with code changes. The other option is to subscribe and consume it as Software as Service (SaS). In this setup, you don't need to take care of anything; you will get some extra polish tooling and reporting.

The deeper dive into the concepts is done in the next section of [overview](overview), or if you would rather see it in action, please jump directly to [live demo](live-demo) page, where is a reference example of the modulith and you can see the platform in motion.