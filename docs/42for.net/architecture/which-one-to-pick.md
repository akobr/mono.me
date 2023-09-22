# Which one to pick?

A simple decision making of which architecture of your project to use:

1. Are you building a new project which is not proven yet and you want to put it on market as soon as possible? => use **Monolith**
2. You already have monolith system which is having problems with performance, maintanace or scalability? => slowly migrate to **Modulith**
3. Your business is extremly depended on performance and scalability. You have demending system as Netflix. => You should ***consider microservices*** -> If you are not 100% sure that you have same size and complexity as wordwide streaming platform, then probably look somewhere else. -> the **Modulith** could be the perfect fit

::: tip
Doens't matter which one you will pick, but you should always try to put all your code under one roof with all technical documentation, infrastructure and support tooling around. Even when you build decentralised application I see no reason why each piece needs to be placed in different spot and use different platform if there is no real reason for it.
:::