# Why not microservices?

Why can microservices be dangerous, and why are they unsuitable for most applications?

::: info
In the beginning, I want to make it clear. I am not saying that microservices are bad or should never be used. They are fun to work with, and there are a lot of exciting challenges that every software engineer loves to solve.
:::

My discomfort is that it is often picked as the initial architecture for most software projects because it is fancy, cool, or famous in the development community. The architecture deserves a rightful place in our developer's hearts, but it must be used only when needed, and all its benefits will be truly used. It shines when we build extra demanding, scalable, and heavy-loaded systems. Something like streaming services or demanding and complex web services with millions of requests per minute. Where we need to be able to manage, maintain, deploy, and scale each micropiece independently. 

If any of the listed benefits are not truly needed, then the architecture of microservices is probably not required, either. You should ask questions if you need that detailed granularity and to maintain the necessary complex platform for microservices, such as service registry, communication mash, complex infrastructure, and CI/CD principles.

Consider a compromise between static and gigantic monolith *(too heavy)* and complex and fragmented microservices *(too complicated)*. Try my recommendation of a [modulith architecture](modulith).

The main problem with microservices is the adoption of totally different thinking, development models, and approaches compared to the monolith. This can be an issue if you plan to migrate from an existing monolith. You have to reteach all your engineers. Please remember that replacing them won't solve it; it will worsen it because new people need [mental model](https://www.baldurbjarnason.com/2022/theory-building/) of your product. Most importantly, make these decisions with your engineering team. In many companies, these decisions are made by management and top managers, not by the people who understand the problem. Another obstacle to consider is a steep learning curve and many changes in the platform, which must happen before the migration.

Don't make a mistake with decentralizing your knowledge base and codebase. When microservices are often implemented, the application is decentralized, and the entire development process, standards, codebase, and teams are split. That is the biggest mistake around the microservices approach: the decentralization of everything, not just the application.

::: tip
Here is an excellent and funny [blog post](https://renegadeotter.com/2023/09/10/death-by-a-thousand-microservices.html) about the dark side of microservices by *Andrei Toranchenko*.
:::

The next page is a simple decision flow about which architecture to pick.