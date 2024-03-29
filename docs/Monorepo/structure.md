# The structure of a mono-repo

> The correct and flexible tree structure of a mono-repo is one of the most important concept in it. It is what define a good mono-repo from a bad one. It allows you to define new tooling and use it in easy way, same like extract or add new content to the mono-repo.

- a mono-repo
  - .artifacts
  - .azuredevops/pipelines
  - .mrepo
  - docs
  - src

The concept of a mono-repository structure is using **worksteads and projects**. Where workstead is a grouping concept of multiple projects or other worksteads. You can imagine a workstead as a business project which contains all necessary coding projects (libraries, packages, and applications) inside.

The most important folder of a repository is `src` which contains all code, all worksteads and projects. Is recommended to use *"siblings folders to source"*, a folder with mirrored structure as `src` and its content is related to worksteads and projects, but not the code itself. Simplest example is `docs` folder with Markdown documentation for the projects and their release notes. The second example could be `.azuredevops/pipelines` to carry all CI/CD pipelines for all deliverables.

Another recommendation is to use one centralized place to hold all artifacts from all the code of the mono-repository. This can be simply used as source for continuous deployment or as a package source for other solution. In the mono-repository structure it is `.artifacts` folder.

> There is a simple naming convention for the non-code content of a mono-repository, each folder or file uses `lower-case-name`, but a code should follow the convention used by the technical stack, e.g. MsBuild and .NET names use `CammelCaseNotation` and it is a good habit to follow namespace/packages structure in folders.

All non-directly related content is placed in folders with a dot on the beginning, for example `.azure` or `.mrepo`. The same notation allow you to add a custom content directly into `src` folder which won't be processed by the tooling.

## Example of src content

Let's consider a simple example of a mono-repository with a solution of table reservation system for restaurants and their customers. The entire problem is separated into two worksteads, one with application for customers where a table reservation can happen and the second part is administration desktop application for restaurants.

- src
  - restaurant
    - Core.Lib
      - src
      - test
    - Admin.App
      - infra
      - src
      - test
      - testasset
  - customer
    - Reservation.App
      - infra
      - build
      - src
      - test
      - testint

As you can see the mono-repository contains two worksteads and three projects. The structure of each project contains at least two folders, `src` and `test`, but this can be extended if you need more content then just the code itself and unit tests for it.

The restaurant workstead is consist from core library and administration application (two projects). The administration application needs a custom asset for the unit tests which is located inside sub-folder `testasset`.

Customer part of the problem is represented as the second workstead with just one project which represent a web-base application with a custom `build` tooling and custom integration tests inside `testint`.

> If you are planning to use [Infrastructure as Code](https://en.wikipedia.org/wiki/Infrastructure_as_code), that is definitely a good idea and really great effort which will pay you back and multiple times. One of possible technology to use is [Pulumi](https://www.pulumi.com/) (my favourite) or [Terraform](https://www.terraform.io/). Please follow the same concept and place all IoC per each project into `infra` sub-folder.

In the visual studio solution we will see a total of seven projects.

- Restaurant.Core.Lib.csproj
- Restaurant.Core.Lib.Tests.csproj
- Restaurant.Admin.App.csproj
- Restaurant.Admin.App.Tests.csproj
- Customer.Reservation.App.njsproj
- Customer.Reservation.App.Tests.njsproj
- Customer.Reservation.App.IntegrationTests.csproj
