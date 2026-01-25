# Pure distributed system

Whenever you need to use a distributed system, it doesn't matter whether the reason is scalability, extensibility, or extreme performance. Always pick native platforms designed from the ground up for native distribution, and never pick microservices.

::: info
The microservices are [the most overrated technology](no-microservices) with extreme complexity vs. output value in software engineering in the last decade.
:::

On the other side of the coin, platforms like **[Orleans](https://learn.microsoft.com/en-us/dotnet/orleans/overview)** or **[proto.actor](https://proto.actor/)** are among the most underrated technologies in software engineering. It is the same as with handicrafts. You can do a lot with generic tools, and it works, but the work is much easier and smoother when you have a specialized tool for each task.

If you're **thinking of building a proper distributed ecosystem, pick Orleans without hesitation**. It is one of the most polished and flawlessly designed platforms and toolsets I have ever worked with. 

Yes, you can still mess it up and create a monster with it. It is the same with any great and flexible tool: *"With great power came a great responsibility."* Most importantly, you have to change your way of thinking, switch your brain from conventional development to an actor-like distributed architecture. Every decent software engineer can transfer with a bit of help.

I'm currently working on multiple unrelated systems that use Orleans as the main architecture, and it is really a blast to work with it when all the recommendations are followed. I'm amazed every time there is a complex technological challenge to solve, how Orleans is designed and easy to extend.

I even started migrating all my personal projects into a single Orleans cluster, and now I'm totally free from any infrastructure, thinking about the increased cloud costs and the boilerplate code I have to write when starting a new application. As a bonus, I get a seamless, perfect option for scaling without any compromise.
