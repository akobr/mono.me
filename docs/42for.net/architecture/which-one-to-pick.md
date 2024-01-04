# Which one to pick?

A simple decision-making of which architecture of your project to use:

1. Are you building a new project that has yet to be proven, and you want to put it on the market as soon as possible? => use **Monolith**
2. Do You already have a monolith system that is having problems with performance, maintenance, or scalability? => slowly migrate to **Modulith**
3. Your business is extremely dependent on performance and scalability. You have a demanding system like Netflix. => You should ***consider microservices*** -> If you are not 100% sure you have the same size and complexity as a worldwide streaming platform, look elsewhere. -> the **Modulith** could be the perfect fit

::: tip
It doesn't matter which one you pick, but you should always put all your code under one roof with all technical documentation, infrastructure, and support tooling around. Even when you build a decentralized application, I see no reason why each piece needs to be placed in a different spot and use a different platform if there is no genuine reason.
:::