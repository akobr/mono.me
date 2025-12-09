# Pure distributed system

Whenever you need to use a distributed system, it doesn't matter whether the reason is scalability, extensibility, or extreme performance. Always pick native platforms designed from the ground up for native distribution, and never pick microservices.

> The microservices are [the most overrated technology](no-microservices) with extreme complexity vs. output value in software engineering in the last decades.

Platforms like **[Orleans](https://learn.microsoft.com/en-us/dotnet/orleans/overview)** or **[proto.actor](https://proto.actor/)** are among the most underrated technologies in software engineering. It is the same as with handicrafts. You can do a lot with generic tools, and it works, but the work is much easier and smoother when you have a specialized tool for each task.

If you're thinking of building a proper distributed ecosystem, pick Orleans without hesitation. It is one of the most polished and flawlessly designed platforms and toolsets I have ever worked with. 

Yes, you can still mess it up and create a monster with it. It is the same with any great and flexible tool: *"With great power came a great responsibility."* Most importantly, you have to change your way of thinking, switch your brain from conventional development to an actor-like distributed architecture. Every decent software engineer can transfer with a bit of help.
