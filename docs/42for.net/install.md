# Install

Here is the shortest way to get your hands dirty and use the tools.

## The .net mono repository

To install the mono-repo CLI, you need to have the dotnet ready on your machine and then run a single command:

``` powershell
dotnet tool install 42.Monorepo.Cli --global --prerelease
```

After that, you can use your favorite IDE and Git tools to build your first .net friendly mono repository. Creating it is as easy as running a single command in your desired target directory:

``` powershell
mrepo new
```

The tool will guide you through the process and help you create a .net mono repository. For more help, please visit [the official documentation](https://github.com/akobr/mono.me/blob/main/docs/Monorepo/README.md).

## The 2S platform

First install the 2S platform command-line interface:

``` powershell
dotnet tool install 42.Platform.Cli --global --prerelease
```

The simplest way is to create an account and build your annotation-based and subscription-based platform today.

``` powershell
sform account
```

To integrate it with your application, use our .net SDK, and learn more about it in this documentation.