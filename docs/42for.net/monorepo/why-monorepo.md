# Why monorepo?

I try to explain reasons why the mono-repo makes sense. As the first thing, let's look for real examples of a mono-repo approach:

- **[Google](https://qeunit.com/blog/how-google-does-monorepo/)**: all web services, 86 TB code-base, 40000 commits per day, 10000 developers, Git
- **[Facebook](https://engineering.fb.com/2014/01/07/core-data/scaling-mercurial-at-facebook/)**: uses Mercurial as source control
- **[Microsoft](https://devblogs.microsoft.com/bharry/the-largest-git-repo-on-the-planet/)**: one single code-base for the entire Windows OS in Git
- other big fishes: *Twitter, [Airbnb](https://qeunit.com/blog/airbnbs-monorepo-journey-to-quality-engineering/), Uber*

## Disadvantages

1. worse scaling (slow on huge sizes) => large clones => longer build times
2. unable to define access control per subfolder *(git doesn't support read permissions for subtrees, only write rules)*

The first disadvantage is valid only for colossal code bases. 4,2% of all mono-repos will need to solve this issue and put some tooling in place. The largest of them use a virtual file system (VFS) to tackle this problem. Google uses [Perforce](https://www.perforce.com/), and Microsoft builds open-source [GVFS](https://en.wikipedia.org/wiki/Virtual_File_System_for_Git) for Git. Facebook uses a different source control, the decentralized system called [Mercurial](https://www.mercurial-scm.org/), which is more suitable for an extremely large mono-repo.

The second point about access control doesn't have a simple solution, but the question should be: *Do I need it?* The most crucial benefit of a mono-repo is the visibility and accessibility of the code. Every developer should be able to see it all.

::: info
It is a particularity to accept and align in the culture and practices to avoid restricting accesses. Shared visibility being a strong point of a monorepo. -- **Google**
:::

Suppose the different levels of access control are absolutely needed. In that case, I prefer to split the single mono-repo into a limited number of smaller mono-repos, which will simulate the other levels. Still, there is no reason for separate access to each project.

### A possible workaround for the lack of access control

Suppose your setup still requires limited access to a specific sub-project. For example, an external worker needs to modify one project, and you don't want to give him full read access to the entire mono-repo. In this scenario, it is essential to keep all your projects as isolated as possible, and when the time comes, just use Git's subtree command to split and create a new repository with the requested sub-project. The separated repo can be configured in any desired way and easily shared with the external worker.

## Advantages

- sharing, **transparency, discoverability**, and visibility
- better debugging + dev testing
- **simplified dependency management** or no dependencies at all
- **reduce code duplication** and complexity
- effective code reviews
- easy refactoring (cross-project changes)
- tooling and **standards**

Some advantages can be beneficial even for fully independent projects, as in this mono-repo. For example, tooling, standards, effective code reviews, and transparency would be applied to any project created under the mono-repo. It is much easier to create a folder than an entire repository and configure everything again.

Nothing in this beautiful world of ours is black or white. Everything can be achieved by custom tooling. All the above advantages can be transferred into a poly-repo environment. Still, starting with the setup where they will work efficiently and automatically is much easier than managing and spending resources on more complex tooling.

## Example of poly-repos

Here, you can read about [the poly-repo approach used in Netflix](https://netflixtechblog.com/towards-true-continuous-integration-distributed-repositories-and-dependencies-2a2e3108c051). They have a lot of excellent tooling for refactoring, dependencies, reviews, and mitigating the advantages of a mono-repo into a poly-repo environment. As you can see, both approaches are promising, and each has its pros and cons. 

My point is that mono-repo has many built-in advantages and simplifications that will work out of the box, just for the small price of following a couple of rules and good habits.

## Something more to read

- [Why we believe mono repos are the right choice for teams that want to ship code faster? | by Pavan Belagatti | Medium](https://medium.com/@pavanbelagatti/why-we-believe-mono-repos-are-the-right-choice-for-teams-that-want-to-ship-code-faster-55f1dea422c7)
- [How Google Does Monorepo - QE Unit](https://qeunit.com/blog/how-google-does-monorepo/)
- [5 ways to configure a monorepo for DevSecOps efficiency - Bridgecrew Blog](https://bridgecrew.io/blog/5-ways-to-configure-a-monorepo-for-devsecops-efficiency/)
- [Airbnb's Monorepo Journey To Quality Engineering - QE Unit](https://qeunit.com/blog/airbnbs-monorepo-journey-to-quality-engineering/)
- [The largest Git repo on the planet](https://devblogs.microsoft.com/bharry/the-largest-git-repo-on-the-planet/)
- [Scaling Git (and some back story)](https://devblogs.microsoft.com/bharry/scaling-git-and-some-back-story/)
- [The Hands-on Mainstream Repo Models You Need To Know - QE Unit](https://qeunit.com/blog/the-hands-on-mainstream-repo-models-you-need-to-know/)
- [Monorepos: Please don’t!. Here we are at the beginning of 2019… | by Matt Klein | Medium](https://medium.com/@mattklein123/monorepos-please-dont-e9a279be011b)
- [Towards true continuous integration: distributed repositories and dependencies](https://netflixtechblog.com/towards-true-continuous-integration-distributed-repositories-and-dependencies-2a2e3108c051)
- [Working with a Monorepo](https://devblogs.microsoft.com/cse/2021/11/23/working-with-a-monorepo/)
- [Mono-repo or multi-repo? Why choose one, when you can have both? | by Patrick Lee Scott | Medium](https://patrickleet.medium.com/mono-repo-or-multi-repo-why-choose-one-when-you-can-have-both-e9c77bd0c668)
- [What is monorepo? (and should you use it?) - Semaphore (semaphoreci.com)](https://semaphoreci.com/blog/what-is-monorepo)
- [Scaling Mercurial at Facebook](https://engineering.fb.com/2014/01/07/core-data/scaling-mercurial-at-facebook/)

### Something to watch on YouTube

- [Uber Technology Day: Monorepo to Multirepo and Back Again](https://www.youtube.com/watch?v=lV8-1S28ycM)
- [From Monorail to Monorepo: Airbnb's journey into microservices - GitHub Universe 2018](https://www.youtube.com/watch?v=47VOcGGm6aU)
- [Dependency Hell, Monorepos and beyond](https://www.youtube.com/watch?v=VNqmHJtItCs)
- [Why Google Stores Billions of Lines of Code in a Single Repository](https://www.youtube.com/watch?v=W71BTkUbdqE)
- [What Is A Monorepo And Why You Should Care - Monorepo vs. Polyrepo](https://www.youtube.com/watch?v=VvcJGjjEyKo)

### Other tooling

- [Git - git-sparse-checkout Documentation (git-scm.com)](https://git-scm.com/docs/git-sparse-checkout)
- [Git subtree: the alternative to Git submodule | Atlassian Git Tutorial](https://www.atlassian.com/git/tutorials/git-subtree)
- [VFS for Git](https://github.com/microsoft/VFSForGit)
- [Mercurial SCM (mercurial-scm.org)](https://www.mercurial-scm.org/)
- [Perforce](https://www.perforce.com/)