# Seed with options command of db-seed

## Problem

`db-seed` in the Aspire host always runs `2sInitDb seed showcase`, and `db-creator` always runs with the staging accounts and the owner fixed at the host's startup (`StagingSeed:Enabled`, `BaseAccount:Id`). Any other set (`exhibit`, `large`), another owner, a dry run or `--again` means leaving the dashboard for the command line and copying the connection string and the vault endpoint from the dashboard into the environment by hand (`docs/Platform/development.md`, "Seed data").

## Current State

- `src/Platform/Aspire.Host/src/AppHost.cs:43-71`: `db-creator` (no arguments, `stagingSeed__enabled` from the mode or `StagingSeed:Enabled`, `baseAccount__id` from `BaseAccount:Id`) and `db-seed` (`seed showcase`, `--owner user_01M41P6G2TTSXXMM88WNVMRQKG` when there is no `BaseAccount:Id` and the staging accounts are seeded); both `WithExplicitStart()`.
- `src/Platform/Aspire.Host/src/UiAdminPreparation.cs`: an existing dashboard command (`Clean reinstall`) that starts and stops resources through `ResourceCommandService`.
- `SeedCommand` of DbCreator: the argument `sets` and the options `--path`, `--organization`, `--project`, `--view`, `--owner`, `--dry-run`, `--force`, `--again`, `--public-pairs-only`.
- Aspire 13.6.4: `CommandOptions.Arguments` declares the inputs of a command, which the dashboard asks for in a dialog before it runs it (`ExecuteCommandContext.Arguments`), and `CommandOptions.ValidateArguments` checks them.

## Proposed Changes

1. **`DbSeedCommands.cs`** in the Aspire host: extension methods `WithSeedOptions` (for `db-seed`) and `WithCreateOptions` (for `db-creator`).
2. **The invocation holder.** Each resource gets a holder of a pending invocation. Its arguments and environment become callbacks (`WithArgs(ctx => …)`, `WithEnvironment(ctx => …)`), evaluated at every start: with a pending invocation they use its values, otherwise the defaults of today. The command sets the holder, starts the resource through `resource-start` and clears the holder when the start returns, so the plain **Start** keeps its current behaviour.
3. **`Seed with options…` on `db-seed`**, the inputs:
   - one checkbox per built-in set, found in `Storyteller/DbCreator/src/data-sets/` at the host's startup (`showcase` checked);
   - `Folder` (text, optional): a `--path` folder;
   - `Owner` (text, optional), prefilled with the default owner (`BaseAccount:Id`, else the staging user when the staging accounts are seeded);
   - `Organization`, `Project`, `View` (text, optional);
   - checkboxes `Dry run`, `Again`, `Force`, `Public pairs only`.
   The validation asks for at least one set or a folder. The command refuses a `db-seed` that is running.
4. **`Create with options…` on `db-creator`**, the inputs: `Base account id` (text, optional, prefilled with `BaseAccount:Id`), passed as `baseAccount__id` when given, and `Seed staging accounts` (checkbox, prefilled with the host's decision), passed as `stagingSeed__enabled`.
5. **Docs**: `docs/Platform/Aspire.Host/local-development.md` (the resource table) and `docs/Platform/development.md` (Seed data).
