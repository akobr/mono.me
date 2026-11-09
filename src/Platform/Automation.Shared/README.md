# Platform automation: shared pieces

`42.Platform.Automation.Shared` (namespace `_42.Platform.Automation.Shared`, `net10.0`, not packable): the pieces the Platform's automation test projects share, on the engine-neutral runtime of the UI automation framework (`src/Libraries/Testing/UI.Automation.Runtime`, no test engine). Its users are the admin UI's suite ([`src/Platform/UI.Admin.AutomationTests`](../UI.Admin.AutomationTests/README.md)) and the CLI's ([spec](../../../docs/Platform/Cli.AutomationTests/specs/2026-11-08%20CLI%20Automation%20Testing%20Project.md), section 3.5).

| Path | Contents |
|---|---|
| `src/Access/AuthKitHostedPage.cs` | The steps on WorkOS' AuthKit hosted sign-in page: `EnterCredentialsAsync(page, email, password)` (Email, Continue or Continue with email, Password, Sign in; typed, not filled), `ContinueButton`, `PasswordOf(user)`, `TypeAsync`, `IsOnHost(url, site)` |
| `src/Access/AuthKitSignInFlow.cs` | The base of a project's `ISignInFlow` "AuthKit": `Name`, the `workos:*` local storage items of the sign-in; the project implements `SignInAsync` (how its application reaches the hosted page and comes back) and `IsSignedOutAsync` |
| `src/Role.cs`, `src/UserRoleAttribute.cs` | `Role` and `[UserRole(Role.X)]` on a test or its class; `UserRoleAttribute.Of(type, method)` reads it (the method's wins) |
| `src/PlatformRoles.cs` | The project role names as `Users:Pool` lists them, `Editing`, `Admin`, `Secrets`, and `RequirementOf(role)`: the pool user a role leases |
| `src/PlatformTargetSettings.cs`, `src/PlatformNames.cs` | The shared keys of `Target` (`Organization`, `Project`, `View`, `RoleProjects`; `RoleProjectOf(role)`, `Validate(errors)`) over the library's `TargetSettings`; a project derives its own `Target` type from it. `BaseUrl` is the admin UI's URL for the admin suite and the API's base (with `/api`) for the CLI's |
| `test/` | `42.Platform.Automation.Shared.UnitTests` (xUnit v3, Shouldly): the target's validation and binding, the roles, `[UserRole]`, the names and the hosted page's checks that need no browser |

Page objects, target checks, API helpers and settings that only one project uses stay in that project; the documentation of the framework is [UI Automation](../../../docs/Libraries/Testing/UI.Automation/readme.md#tests-without-a-browser-automationtest).

```bash
dotnet build src/Platform/Automation.Shared/src
dotnet test --project src/Platform/Automation.Shared/test
```
