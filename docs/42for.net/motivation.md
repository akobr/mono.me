# Motivation

Hi there, thank you for coming here! The purpose of this website is to show you that you can **manage your .net project in a different way** than the mainstream is doing it and it can be more elegant or at least the same. I'm not saying that the trends in software development are wrong but sometimes just too complex for our problems. The main reason for this content is to plant a seed in your head and hopefully, you will start thinking more out of the box and start questioning if every buzzword in IT is always the best way and the only way to go.

## Why am I doing this? 

I always like to question concepts and compare them to be sure that the used one is the best compromise between complexity and benefits. Think about me as a devil's advocate who likes to do things in different ways because that is how you get the most fun from architecting and implementing software projects. Software engineering is an exact science and you should do everything based on data and have real reasons for it, not just your personal preferences or gut feeling.

::: tip
The **best balance between complexity and flexibility** is the golden vein which is super difficult to find and follow.
:::

## To whom this text is addressed? 

Basically, anybody who wants to try to learn about my ideas and to slightly different thinking.

Most fitted are small and middle-sized **companies that serve one or multiple products as a service** for a lot of customers. This environment is exactly where I was operating for most of my professional history.

## The story

My correct company went through a very **common transformation during its existence**. They started offering finance services for a limited number of customers. The entire product has been implemented as an ASP.NET web application in a monolith structure. From the beginning, hosting was on-premise, on real hardware, and later in VMs. 

::: info
Nothing wrong happened, yet. The use of monolith as initial architecture is totally fine. To figure out if your product is going to be a success or not, the most important is the speed to market. Bug-free principles and clean code are not important in that phase of the product. You should think only about how to deliver MVP as soon as possible! The simple architecture and work processes are always the best option.
:::

In couple of years, when more and more customers came in and the age of the cloud rose, people started thinking if this was the best setup. The maintenance is quite heavy, it's difficult to know what is used by whom and the monolith is challenging to build and deploy.

::: info
This is the standard phase one of the problems of most companies which are in growth and the product is success.
:::

Standard precautions have been taken. The monolith is partialy frozen and most of the time just maintained, all new subsystems are always built independently of the monolith as separated repositories and applications.

::: info
Now, we have the mistake number one! I name this phase as *"uncontrolled decentralisation of a monolith"*. The company still has limited resources and only 10-20 engineers and if you allow this wild west separation dance, you will end up in a really unconfortable place. Only thing what will happen is there will be a lot of one-man shows becasue you don't have enough resources to do work in groups and every new application will have totally different bitter taste, because is unique based on the author. Many duplications between multiple repositories and even bigger nightmare to maintain it and keep runing. Even worse things will hapen when somebody decides to leave the company.
:::

## The solution

I will split my talking into two main sections. The first one is about how to manage the most valuable essence of your business. The codebase. I think it is really importan to master it because it can be the smart and valuable core of the platform around your products. The second section will be a talk around architecture and how to tame a famous beat of monolith, this is very often a hot topic in middle size companies with a sucesful product.

### Codebase structure

Why I think the structure and way how you manage your knowhow and code is important? Simply because if you don't have bottomless sources of money for outsourcing or platform agnostic teams then you should put some rules and standards in place. It is much easier to transfer responsibilities between teams and members when they use similar platform and technologies.

As a developer I love transparency and visibility of any code which I wish to see. It doesn't matter if I need to modify it or just simple look under the hood to undestant how a subsystem is working. Developrs are qurious creatures which want to undestant how things are working, and there is nothing more annoing then no possibility to see guts of API call which I want to make from a neighberhood subsystem. 

::: info
The horizontal slices of the application or company structure are the worse and the rich history already show us. That is why we always talk about full-stack development and don't do separation like backend and frontend. Same should be with the test structure, it is the worse thing to try split development, qa or infrastructure into separated silos or teams. That is going to be always workse and slower. It is much better to keep everything on one place and allow to everyone to dig deep as they wish. QA people needs to be close to developers as possible and instrastructure should not be taken on side by dev ops enginers, that is a totaly same mistake.
:::

All this talk brings me to first decision how to manage the codebase and its access. I'm a huge fun of mono repository. I think it is briliant and it always force everybody in a company to be transparent about everything as much as possible. Which, I think it is the most important aspeck when we talk about code! I like Google's phylosophy on restricting accesses:

> It is a particularity to accept and align in the culture and practices to avoid restricting accesses. Shared visibility being a strong point of a monorepo. -- **Google**

As I mentioned many times nothing is black or white. Not even monorepo is perfect. There are a lot of possible disadvantages, but they are in shadows of all the positive which comes out of the box with it.

::: details
In my carrier I was around of 3 different transformations from poly-repo state to mono-repo. It was always same, most of the people around are against it, I personally think they somehow connect monorepo with monolith, but these two things are totally unrelated. Major problem is that most of people are scared of any change, we have it somehow in our nature. In the end, when the transformation has been done, eveybody admire that the final shape was much batter then the previous mess.
:::

Both aproches will need some smart tooling to make your daily work easy and smooth as possible. Both of them can be done in perfect way as a lot of companies already show it to us. For example Netflix is quite good in poly-repo aproach, they have a tuns of awesome tooling which makes the poly-repo setup very handy. In the same time, that is the reason why I think it is not perfect, in fact it is a worse option then mono-repo. You can imagine poly-repo as an intergalactic union which needs to take care of multiple planets in multiple glaxies or on the other hand you have much more strict union on a single planet. Yes, you have probably more freedom in the galaxy but in the same time the rules, standards and tools which are unsed on the single planet can be much simplier. Imagine a ship to transfet containers, it is probably much easier to build an ocean ship to transfer container on the planet then build a starship to cruite between galaxies.

::: details
Faithless developers are always argumenting against monorepo with problem of size and complexity of build. When these problem are real? How big is your codebase even when you put everything together? Is it close enough to Google's or Microsoft's mono-repos?
:::

### Architecture

TBC