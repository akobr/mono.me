# Modulith

The modulith is trying to be the compromise and the middle point between two end-poles, the [monolithic application](https://en.wikipedia.org/wiki/Monolithic_application) and the other side, the [microservices](https://en.wikipedia.org/wiki/Microservices) architecture. The name comes from the synergy between the terms **modu**le and mono**lith**.

Term module is entirely overused in software engineering and has many meanings. I will use **satellite** to avoid confusion when discussing one piece of a decentralized modulith application. The second important term in the concept is **ecosystem**, which represents the entire product as a set of satellites. It is a complete picture of the modulith architecture.

## Differences

The main difference between monolith vs. modulith is the decentralization, where multiple satellites are built, managed, and deployed independently to each other. There isn't a specific rule on how each satellite should look and how big it needs to be. It can vary, and the business needs of the product should define its shape. If we put them side-by-side instead of having one giant blob *(a black box organism)*, we have **a living ecosystem of multiple and variant satellites**.

``` mermaid
%%{init: {"flowchart": {"htmlLabels": false}} }%%
flowchart LR
  monolith(("`one large single application, the **monolith**`"))
  subgraph modulith [modulith]
    A(("`large 
    satellite A`"))
    B(("`medium
    satellite B`"))
    C((tiny C))
    D((small D))
    E((huge satellite E))

    A --> C
    A --> D
    B --> D
    D --> E
    B --> E
  end

  monolith ==> modulith
```

This concept can sound similar to the famous architecture of microservices. Don't be mistaken; it is quite different. The core idea of splitting the monolith into multiple pieces is the same, but that is about it. It is more relaxed than microservices, and instead of having thousands of microscopic parts, you should think more about your business needs and create an adequate number of sized satellites. When you step back and think, it makes perfect sense, why we should create a complex "mess" of countless microservices with entangled dependencies.

``` mermaid
flowchart TB
    S1 --> S4
    S2 --> S1
    S2 --> S3
    S3 --> S4
    S3 --> S9
    S4 --> S5
    S6 --> S7
    S6 --> S16
    S8 --> S6
    S8 --> S9
    S8 --> S19
    S10 --> S2
    S10 --> S8
    S10 --> S11
    S11 --> S5
    S12 --> S10
    S12 --> S13
    S13 --> S16
    S14 --> S13
    S16 --> S15
    S14 --> S17
    S17 --> S15
    S17 --> S19
    S7 --> S17
    S7 --> S9
```

When flexibility and scalability are needed only in specific areas of your ecosystem, you can keep some business concepts in centralized silos and add interfaces and communication pipelines between sub-systems where it is truly unnecessary. The silos are defining boundaries between satellites. When the ecosystem structure is not that granular and complex, even the understanding is easy to grasp, and the platform can be more straightforward.

``` mermaid
%%{init: {"flowchart": {"htmlLabels": false}} }%%
flowchart TB
    S1(("`medium
    satellite S1`"))
    S5(("`large
    satellite S5`"))

    S1 --> S4
    S2 --> S1
    S2 --> S3
    S3 --> S4
    S3 --> S5
```

Managing five actors compared to twenty-ish will be easier, and the necessary flexibility, scalability, and deployability are still achieved. The modulith is the ultimate compromise between monolith and microservices.

::: tip
When planning to decentralize an application, always split it only logically and keep everything else still centralized, especially the [codebase](../monorepo/introduction).
:::

## Everything is about balance

Let's compare the generality of the three architectures.

``` mermaid
flowchart LR
  left[specificity]
  right[generality]

  flexibility([1. flexibility])
  style flexibility stroke-width:0px
  complexness([2. complexness])
  style complexness stroke-width:0px
  limitations([3. technical limitations])
  style limitations stroke-width:0px
  maintenance([4. maintenance/cost])
  style maintenance stroke-width:0px
  value([5. bussiness value])
  style value stroke-width:0px

  left <----------> right
  
  left -.less.- flexibility
  flexibility -.more.....- right

  left -.less.- complexness
  complexness -.more.....- right

  left -.more.- limitations
  limitations -.less.....- right

  left -.less.- maintenance
  maintenance -.more.....- right

  left -.same.- value
  value -.same.....- right
```

1. The flexibility and abstraction are growing with the generality of the system.
2. The complexity goes hand-in-hand with flexibility and generality. We can put abstraction everywhere and make every part and aspect super flexible, but we add complexity to the final solution with these steps. The solution is more and more difficult to follow and understand.
3. More specific areas of the system will be more limited and contain more technical debts.
4. With better abstraction and flexibility, the platform complexity will grow, resulting in more things to maintain, and it would be visible in the final cost. More tooling is needed to handle more generic systems.
5. From a business and company management point of view, the product's final value won't change that much on either side.

The already existing monolith product is generating its value, and moving it from left to right is a non-trivial effort, which can even be questionable from a business value point of view. Software development was, is, and always will be about balancing complexity and flexibility perfectly. Why should it be different from the architecture? If we put all three options on the same scale, we get this:

``` mermaid
flowchart LR
  left[specificity]
  right[generality]

  monolith([monolith])
  style monolith stroke-width:0px
  modulith([modulith])
  style modulith stroke-width:0px
  microservices([microservices])
  style microservices stroke-width:0px

  left --- monolith ---- modulith ---- microservices --- right
```

Why not compromise between both ends with the possibility of staying closer to the left or right side if our situation requires it? You should always balance between flexibility and complexity, where you can lean to one or another side if needed.

Some apply for a new project on a green field. Would you like to put together MVP in no time? Pick some spots close to the left, or be bold to start with a monolith. Why not, when time is the main essence? If you are more comfortable, still pick someplace in the middle for the best balance. When should we pick the right side? Only when we have genuine and substantiated reasons for a highly demanding product. By the way, this will rarely happen on the birth of a product.

Please apply the same logic to ALL technical and engineering decisions. It is that simple! Let's do one more example about infrastructure. Do you need to put all your satellites into containers, build non-trivial Kubernetes clusters, and manage everything in Terraform, which is probably missing from the skill list of each engineer on your team? Again, a compromise is the best. Using containers is always intelligent, simplifies deployment, reduces environment differences, etc., but the clusters and Terraform can be overkill. Maybe you don't need infrastructure, and a serverless approach is the ultimate answer. You will still be somewhere in the middle of the scale, but your ecosystem is flexible and scalable enough for much less of your effort. 

How often have I seen a simple system that could be done as a simple web API with one or two serverless functions? Many times, and it could be baked in one or two weeks. However, a company invested millions into teams of contractors that built a highly complex beast of microservices that can be scaled beyond the stars. The maintenance is costly, and nobody needs it.
