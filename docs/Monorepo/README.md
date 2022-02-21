# mono-repository

This project is trying to tackle problems around a mono-repository and .NET technical stack, but not exlusively. The other goal is to put together useful tooling for a monorepo which is already out there and explain best practices which I recomend to use.

## The structure

- monorepo
  - .artifacts
  - .azure/pipelines
  - .mrepo
  - doc
  - src

Main concept of a mono-repository structure is using workstreads and projects. Where workstead is a grouping concept of multiple projects or other worksteads. You can imagine a worstead as a business project which contains all necesary coding projects (libraries, packages and applications) inside.

The most important folder of a repository is `src` which contains all code, all worksteads and projects. Is recomended to use *"siblings folders to source"*, a folder with mirrored structure as `src` and its content is related to worskteads and projects, but not the code itself. Simplest example is `doc` folder with Markdown documentation for the projects and their release notes. The second example could be `.azure/pipelines` to carry all CI/CD pipelines for all deliverables.

Another recomendation is to use one centralized place to hold all artifacts from all the code of the mono-repository. Then it can be simply used as source for continous deployment or as a package source for other solution. In the mono-repository structure is `.artifacts` folder for these needs.

> There is a simple naming convention for the non-code content of a mono-repository, each folder or file uses `lower-case-name`, but a code should follow the convention used by the technical stack, e.g. MsBuild and .NET names use `CammelCaseNotation` and the code should follow namespace structure.

All non-directly releated content is placed in folders with a dot on the beginning, for example `.azure` or `.mrepo`. The same notation allow you to add a custom content directly into `src` folder which won't be processed by the tooling.

### Example of src content

Let's consider a simple example of a mono-repository with a solution of table reservation system for restaurants and their customers. The entire problem is separated into two worksteads, one with application for customers where a table reservation can happen and the second part is administration desktop application for restaurants.

- src
  - restaurant
    - Core.Lib
      - src
      - test
    - Admin.App
      - src
      - test
      - testasset
  - customer
    - Reservation.App
      - build
      - src
      - test
      - testint

As you can see the mono-repository contains two worsteads and three projects. The structure of each project contains at least two folders, `src` and `test`, but this can be extended if you need more content then just the code itself and unit tests for it.

The restaurant workstead is consist from core library and administration application (two projects). The administration application needs a custom asset for the unit tests which is located inside sub-folder `testasset`.

Customer part of the problem is represented as the second worksted with just one project which represent a web-base application with a custom `build` tooling and custom integration tests inside `testint`.

In the visual studio solution we will see a total of seven projects.

- Restaurant.Core.Lib.csproj
- Restaurant.Core.Lib.Tests.csproj
- Restaurant.Admin.App.csproj
- Restaurant.Admin.App.Tests.csproj
- Customer.Reservation.App.njsproj
- Customer.Reservation.App.Tests.njsproj
- Customer.Reservation.App.IntegrationTests.csproj

## Entire toolset

The heart of the toolset is a CLI application served as dotnet tool under the command `mrepo`. This powerfull command helps you to  manage, create and release everything inside a mono-repository.

## Configuration

Basic configuration of the mono-repository is stored in `mrepo.json` file in the root of the repository. Detailed pieces and custom scripts are located in folder `.mrepo`.
