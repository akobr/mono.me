# Configuration of the monorepo

All configuration for the monorepo, his behaviour and CLI toolset is done by one JSON config file `mrepo.json` in the root of the repository.

```json
{
  "repo": {
    "name": "mono.me",
    "description": "My personal mono-repository with most of my projects.",
    "prefix": "Company", // optional and custom namespace prefix to use for all projects
    "features": [
        // a list of features used in the mono-repo
        "git-version",
        "packages",
        "build-traversal",
    ],
    "scripts": { /* add any global scripts here */ }
  },
  "release": {
    "branches": [
      "^refs/heads/main$"
    ],
    "changes": {
      "major": [ /* custom major commit message types */ ],
      "minor": [ "feat" ],
      "patch": [ "fix", "perf", "refactor" ],
      "harmless": [ "style", "test", "build", "ci", "docs", "chore" ]
    },
    // url template used for mapping of issue IDs in git history
    "issueUrlTemplate": "https://app.clickup.com/t/{0}"
  },
  "types": {
    // add custom types of items here (workstead, project)
    "dotnet-project": {
      "custom": {
        "filePattern": "*.*?proj",
        "useFullProjectNames": false
      },
      "scripts": {
        "build": "dotnet build src",
        "clean": "dotnet clean src",
        "pack": "dotnet pack src",
        "restore": "dotnet restore src",
        "run": "dotnet run src",
        "test": "dotnet test test"
      }
    }
  },
  "items": [
    // your worksteads/projects configuration is going to be here
    {
      "path": "src/Monorepo",
      "name": "Monorepo",
      "description": "Tooling around mono-repository."
    },
    {
      "path": "src/Cetris",
      "name": "Cetris",
      "description": "The intersection of the legendary game Tetris + Command line = Cetris."
    }
  ]
}

```
