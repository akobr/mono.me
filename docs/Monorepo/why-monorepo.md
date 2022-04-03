# Why to consider a mono-repo approach

I try to explain reasons why I think the mono-repo make sense. As the first thing, let's look for real examples of a mono-repo aproach:

- **[Google](https://qeunit.com/blog/how-google-does-monorepo/)**: all web services, 86 TB code-base, 40000 commits per day, 10000 developers, git
- **Facebook**: uses Mercurial as source control
- **Microsoft**: one single codebase for entire Windows OS in git
- another big fishes: *Twitter, Airbnb, Uber*

## Disadvantages
1. worse scaling (slow on huge sizes) => large clones => longer build times
2. unable to define access control per subfolder
  - git doesn't support read permissions for subtrees (only write rules)

The firsts disadvantage is valid for really large codebases. I would say probably like 4% of all mono-repos will need to solve this issue and put some tooling in place to solve it. Some of the largest use virtual file system (VFS) to tackle this problem. Google uses XXX and Microsoft builds open-source VFS for Git. Facebook is using different source control, the decentralized system called Mercurial, which is more suitable for a large mono-repo.

The second point about access control currently doesn't have a simple solution, but the question should be do I really need it? The most important benefit of a mono-repo is the visibility and accessibility of the code. I would say every developer should be able to see it all.

> It is a particularity to accept and align in the culture and practices to avoid restricting accesses. Shared visibility being a strong point of a monorepo. -- Google

### Posible workaround for the lack of access control

If your setup still require a limited access of a specific sub-project, for example an external worker need to do some modification in one of them and you don't want to give him a full read-access to the entire mono-repo. In this scenarion is important to keep all projects isolated as possible and when the time comes just simple use git's subtree command to split and create new repository with the requested sub-project. The separated repo can be configured in any desared way and shared with the external worker quite easly.

> Later on I will describe how to create this separated repo, what difficultis you can discover, and how to merge it back to a mono-repo after all the external work is done.

## Advantages

- sharing, transparency, discoverability and visibility
- better debugging + dev testing
- simplified dependency management or no dependencies at all
- reduce code duplication and complexity
- effective code reviews
- easy refactoring (cross-project changes)
- tooling and standards

Some of the advantages can be beneficial even for a totally independend projects, same like in this mono-repo, for example *tooling, standards, effective code reviews and transparency* would be forced on any project which is created under the mono-repo. It is much easier just create a folder rather then entire repository and configure everyting again.

Nothing in this beautiful world of ours is black and white, everything can be achived by custom tooling. All listed advantages in the list above can be achived even for poly-repo environment, but it is much easier to start with the setup where all of them will work easly and automatically rather than manage and spend resources for more tooling.

## Example of poly-repos

Here you can read about poly-repo aproach which has been taken in Netflix. They have a lot of nice tooling for refactoring, dependencies, reviews and so on to mitigate the advantages of a mono-repo to poly-repo environment. As you can see both approches are good and each of them has his own pros and cons. My point is that mono-repo comes up with a lot of build in advantages and simplifications which will work basically out of the box, just for price of following a couple of rules and good habits.

## Something more to read

- [Why we believe mono repos are the right choice for teams that want to ship code faster? | by Pavan Belagatti | Medium](https://medium.com/@pavanbelagatti/why-we-believe-mono-repos-are-the-right-choice-for-teams-that-want-to-ship-code-faster-55f1dea422c7)
- [How Google Does Monorepo - QE Unit](https://qeunit.com/blog/how-google-does-monorepo/)
- [5 ways to configure a monorepo for DevSecOps efficiency - Bridgecrew Blog](https://bridgecrew.io/blog/5-ways-to-configure-a-monorepo-for-devsecops-efficiency/)
- [Airbnb's Monorepo Journey To Quality Engineering - QE Unit](https://qeunit.com/blog/airbnbs-monorepo-journey-to-quality-engineering/)
- [Mono-repo or multi-repo? Why choose one, when you can have both? | by Patrick Lee Scott | Medium](https://patrickleet.medium.com/mono-repo-or-multi-repo-why-choose-one-when-you-can-have-both-e9c77bd0c668)
- [The Hands-on Mainstream Repo Models You Need To Know - QE Unit](https://qeunit.com/blog/the-hands-on-mainstream-repo-models-you-need-to-know/)
- [Monorepos: Please don’t!. Here we are at the beginning of 2019… | by Matt Klein | Medium](https://medium.com/@mattklein123/monorepos-please-dont-e9a279be011b)
- [Git - git-sparse-checkout Documentation (git-scm.com)](https://git-scm.com/docs/git-sparse-checkout)
- [Git subtree: the alternative to Git submodule | Atlassian Git Tutorial](https://www.atlassian.com/git/tutorials/git-subtree)
- [Mercurial SCM (mercurial-scm.org)](https://www.mercurial-scm.org/)
- [What is monorepo? (and should you use it?) - Semaphore (semaphoreci.com)](https://semaphoreci.com/blog/what-is-monorepo)