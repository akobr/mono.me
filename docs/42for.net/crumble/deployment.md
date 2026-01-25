# How to deploy and do infrastructure

> *Under construction...*

Even though the Azure serverless platform is interesting, there are many drawbacks. It is difficult to split deliverables, handle service plans, and scale properly. At the same time, there are many technical limitations, and some basic features are treated as premium, e.g., a static IP address.

What I recommend is to manage a centralized Orleans framework cluster where you can place all your event-driven jobs across all your products, with simple, infinite scaling.