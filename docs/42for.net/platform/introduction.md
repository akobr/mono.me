# Introduction of 2S platform <Badge type="danger" text="35% done" />

::: danger
The platform is under development, see [the road map](road-map).
:::

The **2S platform**, as I call it, should help you to manage your products and complex monolith application(s). The **2S** represents **S**implicity and **S**ubscription-based platform.

::: info
I'm going to use in the text a lot of expressions which are described in the section about the [modulith](../architecture/modulith) architecture, for example **ecosystem** and **satelite**. If you didn't know them, I recomend you to read that section first.
:::

The platform is used to describe the relationships and the building blocks in you ecosystem plus it should help you to manage it by smart monitoring, centralized configuration, and real-time event-based logs of the system. Everything is done by three independent actors, which can be used in combination or separately:

- Storyteller
- Supervisor
- Scheduler

The storyteller describes entire business structure and dependecies in the ecosystem and it acts as documentation and the subscription serve service for the runtime. The second part, the supervisor, is smart and real-time watcher of the system. Last and most specific actor is a flexible scheduler of jobs or work units if a lot of time based processing is needed and a monitoring of them in a sustainable way is required.

::: tip
The platform is designed in the way that it should help you to undestand the modeled ecosystem, by simple documenting principles. This is extremly handy when you bring new faces to your team. Another supperior concept is to speed up reverse engineering, and debuging in case of incident. As I mentioned many time the **transparency and visibility** is one of the most important property of a healty product.
:::

The actors are open-source, which allows you to deploy it on-premise, under your own cloud account or into any hybrid environment. If you need to bend some part, please be your guest and create a PR with code changes. The other option is to subscribe and consume it as Software as Service (SaS). In this setup you don't need to take care about anything and you will get some extra polish tooling and reporting.

The deeper dive into the concepts is done on next section of [overview](overview) or if you rather to see it in action, please jump directly to [live demo](live-demo) page where is described my reference example of monolith and you can see the platform in motion.
