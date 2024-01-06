# Motivation

Hi there, thank you for coming here! The purpose of this website is to show you that you can **manage your .net project in a different way** than the mainstream is doing it, and it can be more elegant or at least the same. The trends in software development are not wrong, but sometimes just too complex for our problems. The main reason for this content is to plant a seed in your mind, and hopefully, you will start thinking more outside the box and questioning if every buzzword in IT is always the best way and the only way to go.

## Why am I doing this? 

I always like to question concepts and compare them to be sure that the used one is the best compromise between complexity and benefits. Think about me as a devil's advocate who likes to do things differently because that is how you get the most fun from architecting and implementing software projects. Software engineering is an exact science, and you should do everything based on data and have actual reasons for it, not just your personal preferences or gut feelings.

::: tip
The **best balance between complexity and flexibility** is the golden vein that is difficult to find and follow.
:::

## To whom is this text addressed? 

Basically, anybody who wants to learn about my ideas and try slightly different thinking.

Most fitted are small and middle-sized **companies that serve one or multiple [products as a service](https://en.wikipedia.org/wiki/Subscription_business_model)** for a lot of customers. This environment is exactly where I was operating for most of my professional history.

## The story

My correct company went through a very **common transformation during its existence**. They started offering finance services for a limited number of customers. The entire product has been implemented as an ASP.NET web application in a monolith structure. From the beginning, hosting was on-premise, on real hardware, and later in VMs. 

::: info
Nothing wrong has happened yet. The use of monolith as initial architecture is excellent. To determine if your product will be a success or not, the most important is the speed to market. Bug-free principles and clean code are not necessary in that phase of the product. You should think only about how to deliver MVP as fast as possible! The simple architecture and work processes are always the best option.
:::

In a couple of years, when more and more customers came in, the age of the cloud rose. People started thinking if this was the best setup. The maintenance is quite heavy. It isn't easy to know what is used by whom, and the monolith is challenging to build and deploy. 

::: info
This is the standard phase one of the initial problems in most growing companies with a successful product.
:::

Standard precautions have been taken. The monolith is partially frozen and maintained. All new subsystems are built independently of the monolith as separated repositories and applications.

::: info
Now, we have the mistake number one! I name this phase as *"uncontrolled decentralization of a monolith"*. The company still has limited resources and only 10-20 engineers, and if you allow this wild west separation dance, you will end up in an uncomfortable place. The only thing that will happen is there will be a lot of one-man shows because you don't have enough resources to work in groups, and every new application will have a different taste because it is unique based on the author. There are many duplications between multiple repositories, and it is an even bigger nightmare to maintain it and keep it running. Even worse things will happen when somebody decides to leave the company.
:::

The uncontrolled decentralization added just more problems to an already complicated product. After this unpleasant experience, many companies could think of even more drastic approaches to change the underlying architecture, hire a new team of people to drive and make decisions about the product, or, even worse, transfer most of the work to external consultants.

::: info
The first approach to changing the architecture must be done smartly, and all the possibilities must be considered carefully. You should have a detailed migration plan that you will follow for a long time because a big-bang approach is impossible in already deployed and used products. For 90% of all products out there, a monolith is a good enough solution when all the edges are polished. The opposite and famous microservices architecture is often a complexity trap and unnecessary. My opinion is elaborated in a separate [article dedicated to microservices](/architecture/no-microservices).

The second approach is even worse and, most of the time, ends up catastrophically. Suppose you will change people who make decisions about your product or transfer the knowledge out of your company to contractors. You will lose the understanding of the product, which is your most valuable piece! The code itself is priceless without it! The theory about building a software product has been explained super nicely by Baldur Bjarnason in his [blog post](https://www.baldurbjarnason.com/2022/theory-building/). I recommend reading it.
:::

Luckily, this was different for the company where I work and proudly. Instead of making some rapid changes or common mistakes, a decision has been made to make less drastic but sustainable changes with an intelligent migration plan. The following section describes what and why we did it. All text here and the offered tooling is the golden output of it. 

::: details
People currently working on your product are the most valuable part of your business, and you should be highly grateful for them. To replace them with consultants or bring in too many new faces, which will overwhelm them, is a silly decision.
:::

## The solution

I will split my talk into two parts. The first is about managing the most valuable essence of your business, the codebase. I think it is vital to master it because it can be the intelligent and valuable core of the future platform around your product. The second part will be a talk about architecture and how to tame a famous beast of a monolith; this is very often a hot topic in middle-size companies with a successful product.

### Codebase structure

Why do I think the structure and way how you manage your know-how and code is important? Simply because if you don't have bottomless sources of money for outsourcing, or usage of platform agnostic teams then you should put some rules and standards in place. It is much easier to transfer responsibilities between teams and members when they use similar platforms and technologies.

As a developer, I love the transparency and visibility of any code that I wish to see. It doesn't matter if I need to modify it or just simply look under the hood to understand how a subsystem is working. Developers are curious creatures who want to undestant how things are working, and there is nothing more annoying then no possibility of seeing the guts of an API call that I want to make from a neighborhood subsystem. 

::: info
The horizontal slices of the application or company structure are the worst. History has already shown us that. That is why we always talk about full-stack development and don't do separation like backend and frontend. The same should be done with the rest of the engineering resources. It is worse to try split development, QA, or infrastructure / deployment into separate silos or teams. The separated structure is going to be slower and introduce communication difficulties. It is better to keep everything in one place and allow everyone to dig deep as they wish. QA people need to be as close to developers as possible, and dev ops engineers should not take infrastructure on the side; that is the same mistake.
:::

All this talk brings me to the first decision on how to manage the codebase and its access. I'm a massive fan of a mono repository. It is brilliant and always forces everybody in a company to be transparent about everything they do. It is the most crucial aspect when we talk about code! I like Google's philosophy on restricting access:

> It is a particularity to accept and align in the culture and practices to avoid restricting accesses. Shared visibility being a strong point of a monorepo. -- **Google**

As I mentioned many times nothing is black or white. Not even monorepo is perfect. There are a lot of possible disadvantages, but they are in the shadows of all the positive that comes *"out of the box"* with it.

Both approaches, mono repo and poly repos, will need some intelligent tooling to make your daily work easy and smooth. Both of them can be done perfectly. For example [Netflix](https://netflixtechblog.com/towards-true-continuous-integration-distributed-repositories-and-dependencies-2a2e3108c051) is quite good in the poly-repo approach, they have a ton of incredible tooling which makes the poly-repo setup very handy. At the same time, think how complex it is to manage and use all these tools and how much effort and money it costs.

You can envision poly-repo as an intergalactic union that needs to take care of multiple planets in multiple galaxies, or, on the other hand, you have a much more strict union on a single planet. Yes, you have more freedom in the galaxy, but rules, standards, and tools required on a single planet can be much more straightforward. Imagine a ship to transfer containers; it is far easier to build an ocean ship to transfer them between continents than build a starship to cruise between galaxies. Simplicity should always win!

::: details
Faithless developers always argue against monorepo with problems of size and complexity of the build. When are these problems real? How big is your codebase even when you put everything together? Is it close enough to Google's or Microsoft's monorepos?

In my career, I was around three different transformations from poly-repo state to mono-repo. It was always the same. Most people around were initially against it. They connect a mono repo with a monolith, but these two things are unrelated. At the same time, most people are scared of any change. We have it somehow in our DNA, but when the transformation finished, everybody always admired that the final shape was much better than the previous mess.
:::

### Architecture

Which architecture model to use is a tricky question. Consider numerous details about your product, but think only a little. I put together [a simple decision list](/architecture/which-one-to-pick).

If you are building MVP or a new project, you must validate and ensure it will fit your expectations well. I recommend taking shortcuts and ways with the lowest friction. In that case, the quick implementation by the most suitable platform and monolith architecture is perfect.

Later, when the product is settled and well-funded, you should start simplifying maintenance and building an excellent living platform around it. There is no reason to wait for more troubles or push this further in the future. The more you wait, the more technical debts will be created. It is the same with all other problems, e.g., when performance or scalability issues float to the surface, quick or partial solutions won't solve them.

::: warning
Most of the time, engineering teams understand the importance of these needs, but it takes a lot of effort to sell them to management. The product's future must start facing technical debts as soon as possible, and this must be made clear to the entire company. If your product is supposed to be vital for a long time and strong enough to compete with young and more modern solutions, you have to keep it in perfect shape. 

Same easily, as you think your product is ahead of the competition. Another flexible, lightweight, and clever startup can come into play and overtake you blazingly fast, and you won't be able to react before it is too late.

Only companies that understand and act fast enough will survive and stay healthy. The concept is called [platform engineering](https://platformengineering.org/blog/what-is-platform-engineering), and it should be applied to every successful software product. It doesn't need to be anything significant. A single resource to improve the technical state and the platform is enough from the start, but keeping it independent and unaffected by the product roadmap is necessary.
:::

The opposite side of a monolith is microservices. It's a totally decentralized and quite complicated structure. Many software companies are taking this path as retribution for monolith problems. That is a short-sighted decision. In most cases, it ends up as described in this nice [blog post](https://renegadeotter.com/2023/09/10/death-by-a-thousand-microservices.html) by *Andrei Toranchenko*. Again, the balance between the complexity and benefits of using microservices is incorrect for most software projects. It can be the right path for highly demanding and scalable problems, but most of 
the time, it is simply not.

As the best and most universal solution, I assume the [modulith](/architecture/modulith), somewhere in between. It is easy to adapt, most suitable for existing products based on a monolith architecture, and easy to start with.

## The output

The output of all our decisions and steps are summarized on these pages. The tooling and platform are available as open-source code under an MIT license. Please feel free to use them or extend them. If you wish to stay in contact, don't hesitate to send me a message on [LinkedIn](https://www.linkedin.com/in/kobrales/) or contact me on [GitHub](https://github.com/akobr).

- [Monorepo CLI](/monorepo/introduction)
- [2S platform as service](/platform/introduction)