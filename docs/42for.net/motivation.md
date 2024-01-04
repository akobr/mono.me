# Motivation

Hi there, thank you for coming here! The purpose of this website is to show you that you can **manage your .net project in a different way** than the mainstream is doing it and it can be more elegant or at least the same. I'm not saying that the trends in software development are wrong but sometimes just too complex for our problems. The main reason for this content is to plant a seed in your head and hopefully, you will start thinking more out of the box and start questioning if every buzzword in IT is always the best way and the only way to go.

## Why am I doing this? 

I always like to question concepts and compare them to be sure that the used one is the best compromise between complexity and benefits. Think about me as a devil's advocate who likes to do things in different ways because that is how you get the most fun from architecting and implementing software projects. Software engineering is an exact science and you should do everything based on data and have real reasons for it, not just your personal preferences or gut feeling.

::: tip
The **best balance between complexity and flexibility** is the golden vein which is super difficult to find and follow.
:::

## To whom this text is addressed? 

Basically, anybody who wants to try to learn about my ideas and try slightly different thinking.

Most fitted are small and middle-sized **companies that serve one or multiple [products as a service](https://en.wikipedia.org/wiki/Subscription_business_model)** for a lot of customers. This environment is exactly where I was operating for most of my professional history.

## The story

My correct company went through a very **common transformation during its existence**. They started offering finance services for a limited number of customers. The entire product has been implemented as an ASP.NET web application in a monolith structure. From the beginning, hosting was on-premise, on real hardware, and later in VMs. 

::: info
Nothing wrong happened, yet. The use of monolith as initial architecture is totally fine. To figure out if your product is going to be a success or not, the most important is the speed to market. Bug-free principles and clean code are not important in that phase of the product. You should think only about how to deliver MVP as fast as possible! The simple architecture and work processes are always the best option.
:::

In couple of years, when more and more customers came in and the age of the cloud rose, people started thinking if this was the best setup. The maintenance is quite heavy, it's difficult to know what is used by whom and the monolith is challenging to build and deploy. 

::: info
This is the standard phase one of some problems in most companies which are growing and the product is success.
:::

Standard precautions have been taken. The monolith is partialy frozen and maintained, all new subsystems are built independently of the monolith as separated repositories and applications.

::: info
Now, we have the mistake number one! I name this phase as *"uncontrolled decentralisation of a monolith"*. The company still has limited resources and only 10-20 engineers and if you allow this wild west separation dance, you will end up in a really unconfortable place. Only thing what will happen is there will be a lot of one-man shows becasue you don't have enough resources to do work in groups and every new application will have totally different taste, because is unique based on the author. Many duplications between multiple repositories and even bigger nightmare to maintain it and keep runing. Even worse things will hapen when somebody decides to leave the company.
:::

The uncontrolled decentralization added just more problems to an already complicated product. After this unpleasant experience, a lot of companies could think of even more drastic approaches to change the underlying architecture totally, hire new team of people to drive and make decisions about the product, or even worse transfer most of the work to external consultants.

::: info
The first approach to changing the architecture needs to be done in a smart way and all the possibilities need to be considered, carefully. You should have a detailed migration plan that you will follow for a long time because a big bang approach is not possible in already deployed and used products. I personally think, for 90% of all products out there a monolith is a good enough solution when all the edges are polished. The opposite and famous architecture of microservices is very often a complexity trap and not needed. My opinion is described in [a separate article dedicated only to microservices](/architecture/no-microservices).

The second approach is even worse and most of the time end up catastrophically. If you are going to change people who makes decision about your product to fast or transfer the knowledge out of your company to contractors. You are going to lose the undestading of the product which is the most valuable what you own! The code itself is totally priceless without it. The theory about the building a software product has been explained super nicely by Baldur Bjarnason in his [blog post](https://www.baldurbjarnason.com/2022/theory-building/). I really recomend to read it.
:::

Luckily this wasn't the case of the company where I'm currently and proudly working. Instead of doing some rapid changes or common mistakes, a decision to take less drastic but sustainable changes with a good migration plan has been made. The next section is describing what and why we did it. All text here and the offered tooling is the golden ouput of it.  

::: details
People currently working on your product are the most valuable part of your business and you should be extremely grateful for them. To replace them with consultants or bring two many new faces which will overhelm them is a silly decision.
:::

## The solution

I will split my talking into two parts. The first one is about how to manage the most valuable essence of your business. The codebase. I think it is really importan to master it because it can be the smart and valuable core of the future platform around your product. The second part will be a talk around architecture and how to tame a famous beat of monolith, this is very often a hot topic in middle size companies with a successful product.

### Codebase structure

Why do I think the structure and way how you manage your know-how and code is important? Simply because if you don't have bottomless sources of money for outsourcing or platform agnostic teams then you should put some rules and standards in place. It is much easier to transfer responsibilities between teams and members when they use similar platforms and technologies.

As a developer, I love the transparency and visibility of any code that I wish to see. It doesn't matter if I need to modify it or just simply look under the hood to understand how a subsystem is working. Developers are curious creatures who want to undestant how things are working, and there is nothing more annoying then no possibility of seeing the guts of an API call that I want to make from a neighborhood subsystem. 

::: info
The horizontal slices of the application or company structure are the worst and the rich history already shows us. That is why we always talk about full-stack development and don't do separation like backend and frontend. The same should be with the rest of engineering resources. It is the worse to try split development, QA, or infrastructure/deployment into separate silos or teams. Separated structure is going to be slower and intruduce communication dificulties. It is better to keep everything on one place and allow to everyone to dig deep as they wish. QA people needs to be close to developers as possible and instrastructure should not be taken on side by dev ops enginers, that is a totaly same mistake.
:::

All this talk brings me to the first decision on how to manage the codebase and its access. I'm a huge fan of a mono repository. I think it is briliant and it always force everybody in a company to be transparent about everything they do. This is the most important aspect when we talk about code! I like Google's phylosophy on restricting accesses:

> It is a particularity to accept and align in the culture and practices to avoid restricting accesses. Shared visibility being a strong point of a monorepo. -- **Google**

As I mentioned many times nothing is black or white. Not even monorepo is perfect. There are a lot of possible disadvantages, but they are in the shadows of all the positive that comes "out of the box" with it.

Both approaches, mono repo and poly repos, will need some smart tooling to make your daily work easy and smooth. Both of them can be done in a perfect way, for example [Netflix](https://netflixtechblog.com/towards-true-continuous-integration-distributed-repositories-and-dependencies-2a2e3108c051) is quite good in the poly-repo approach, they have a ton of awesome tooling which makes the poly-repo setup very handy. At the same time, that is the reason why I think it is not perfect, in fact, it is a worse option than mono-repo. You can imagine poly-repo as an intergalactic union that needs to take care of multiple planets in multiple galaxies or on the other hand you have a much more strict union on a single planet. Yes, you have probably more freedom in the galaxy but rules, standards, and tools that are required on a single planet can be much simplier. Imagine a ship to transfer containers, it is  easier to build an ocean ship to transfer them between continents rather than build a starship to cruise between galaxies.

::: details
Faithless developers always argue against monorepo with problems of size and complexity of the build. When these problems are real? How big is your codebase even when you put everything together? Is it close enough to Google's or Microsoft's monorepos?

In my career, I was around of 3 different transformations from poly-repo state to mono-repo. It was always the same, most of the people around are initialy against it, I personally think they somehow connect monorepo with monolith, but these two things are totally unrelated. In same time, the most of people are scared of any change, we have it somehow in our nature. In the end, when a transformation has been done, everybody admires that the final shape was much better than the previous mess.
:::

### Architecture

Which architecture base model to use is a tricky question. You should consider many small details about your product, but again don't overthink too much. I put together [a simple decision list](/architecture/which-one-to-pick).

If you are building MVP or a totally new project which you need to first validate and make sure it will be a good fit with your expectation. I would always recommend to take shortcuts and a way with lowest friction. In that case, the quick implementation by most suitable platform and monolith as architecture is perfect.

Later, when the product is settled and well funded, you should start thinking about how to make the maintanance simplier, how to build a nice and living platform around it. There is no reason to wait for more troubles or push this further in the future. More your wait, more technical debts will be created. Same with all other problems, e.g. when performance difficulties float to the surface, or scalability issues, they won't be solved by a quick or partial solutions.

::: warning
Most of the time, engineering teams undestand the importance of these needs but it is difficult to sell them to management. It is really crusial for the product future to start facing them as soon as possible and make this clear in entire company. If your product should be vital for long time and strong enough to compite with yunger and more moders solutions, you have to keep it in a perfect shape. 

Same easily, as you think your product is ahead of the competition. Another flexible, lightweight, and smart startup can come into play and overtake you blazingly fast and you won't be able to react before is too late.

Only companies which undestand and act fast enough will survive and stay healthy. The concept is called [platform engineering](https://platformengineering.org/blog/what-is-platform-engineering) and it should be aplicated on every successful software product. It doesn't need to be anything big, just to have a single resource which can be used for improving technical state and platform. Important is just to keep it independent and unaffected by the product roadmap.
:::

The oposite side of monolith are microservices. Totally decentralised and quite complicated structure. A lot of software projects are taking this path as retribution for monolith problems. That is a short seeing decision which in most of the cases ends up as described in this nice [blog post](https://renegadeotter.com/2023/09/10/death-by-a-thousand-microservices.html) by Andrei Toranchenko. Again, the balance between complexity and benefits is not correct in most of the projects. For extreme demanding and scalable problems it can be the right path, but most of the time it simple isn't.

As the best and most universal solution I assume [modulith](/architecture/modulith) which is somewhere in between. It is easy to adapt and is is most suitable for already existing projects based on monolith architecture and not so difficult to start with.

## The output

The output of all our decisions and steps are sumarized on these pages. The tooling and platform is available as open-source code. Please feel free to use them or extend them. If you wish to stay in contact, don't hasitate to send me a message on [LinkedIn](https://www.linkedin.com/in/kobrales/) or contact me on [GitHub](https://github.com/akobr).

- [Monorepo (mrepo CLI)](/monorepo/introduction)
- [2S platform as service](/platform/introduction)