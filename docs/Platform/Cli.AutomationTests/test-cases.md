# Test Cases of the CLI

The functional test cases of the `sform` CLI in Testiny project `2SC` (id 2), and the automated test of each in `src/Platform/Cli.AutomationTests/src/Tests/`. Every case has type Functional, status Ready for Test and automation Automated. Each Testiny folder is a folder of `Tests/`, and each sub-folder is a test class. The plan is [2026-11-08 CLI Automation Testing Project](specs/2026-11-08%20CLI%20Automation%20Testing%20Project.md) (Linear [424-215](https://linear.app/424fun/issue/424-215/create-cli-automation-testing-project-sform-against-a-real-backend)).

This is the **draft** of the catalogue (phase 4). The **Testiny** column is filled in when the cases are created in Testiny, and the **Test** column holds the planned method names until the tests exist.

- **Reference**: `<section>-<sub-folder>-<n>`. It starts the Testiny precondition and the comment above the test.
- **Priority**: Elementary (in the PR-gate set, `[Elementary]`), High, Medium or Low.
- **Role** (`[UserRole]`):
  - Any: any pool user.
  - Reader, Contributor: exactly that role, through `Target:RoleProjects`.
  - Editor: Contributor or higher.
  - Secrets: ContributorWithSecrets or higher.
  - Admin: Administrator or Owner.
  - Owner.
  - Machine: no user. Machine credentials (`STORYTELLER_*`) with the scopes the case names.
- **Mode**:
  - U: the leased user's signed-in `SFORM_HOME`. This is the default; the column lists the other modes.
  - F: a fresh, empty `SFORM_HOME` (`[FreshSignIn]`).
  - M: machine credentials, no user state.
  - T: run in a pseudo terminal (`SformRunOptions.Terminal`), with the prompt answered by the test.
  - E: the fake editor of `editor.config.json` (spec §4.3).
  - P: in a disposable project (`[DisposableProject]`).
  - X:`<Feature>`: skips unless `Target:Features` lists the feature, and the gap is in `coverage-exceptions.json`. The features: `Byok` (a customer key in an Azure Key Vault), `Hsm` (HSM-backed keys), `KeyVaultReference` (a vault that the target can read), `Registration` (an unregistered pool user whose account the run may create), `MultiOrganization` (a pool user in two organizations).
- **Covers**: the options, arguments and aliases the case checks the effect of (`[Covers]`, spec §4.2). `arg0`, `arg1`, … are the positional arguments. Every command, option, argument and alias of the CLI appears at least once in this column; `CommandCoverageTests` enforces it.

Rules shared by every case:

- A test starts with no default project: it passes `-p <org>.<project>` (and `-v` when it says so), so the context options of `BaseContextCommand` are part of every context command's cases. The context cases in [Context and Errors](#context-and-errors) check the options themselves.
- The expected exit codes are those of `src/Platform/Cli/src/ExitCodes.cs`: 0 success, -1 input parsing, 1 crash, 2 wrong input, 3 service failure, 40 aborted, 42 no work needed, 43 interaction needed, 44 unauthorized, 45 partial result. "Fails" without a code means a non-zero code other than 1; a crash (1) fails every case.
- Annotation keys, names and values are generated (Bogus, seeded) under the target project and are not deleted, except where a case deletes them. `<rst>` stands for a generated responsibility key such as `rst.cli-3f9a`, and `<key>` for a generated key-value key.
- Steps are written as the command line, without `sform`. An expectation on output is a check on stdout unless it says stderr.

Cases per section:

| Section | Folder | Cases |
|---|---|---|
| [Sign-in](#sign-in) | `Tests/SignIn` | 14 |
| [Account and Access Points](#account-and-access-points) | `Tests/Account` | 15 |
| [Machines](#machines) | `Tests/Machines` | 29 |
| [Shared Certificates](#shared-certificates) | `Tests/SharedCertificates` | 5 |
| [Annotations](#annotations) | `Tests/Annotations` | 37 |
| [Configurations](#configurations) | `Tests/Configurations` | 31 |
| [Templates](#templates) | `Tests/Templates` | 8 |
| [Schemas](#schemas) | `Tests/Schemas` | 20 |
| [Key-Values](#key-values) | `Tests/KeyValues` | 50 |
| [Context and Errors](#context-and-errors) | `Tests/General` | 10 |
| **Total** | | **219** |

## Sign-in

### Device Code (`DeviceCodeSignInTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| SGN-DEV-01 | – | A pool user signs in with the device code and sees the account summary | Elementary | Any | F | `account`, `-o` | `DeviceCode_SignIn_ShowsAccount` |
| SGN-DEV-02 | – | The aliases `access` and `login` report the signed-in user like `account` | Medium | Any | | `access`, `login` | `Account_Aliases_ReportSignedInUser` |
| SGN-DEV-03 | – | Without `SFORM_DEVICE_CODE_OUTPUT` the prompt is text with the URL, the code, the complete URL and the expiry | Medium | Any | F | `account` | `DeviceCode_TextPrompt_ShowsCodeAndExpiry` |
| SGN-DEV-04 | – | `-b` opens the complete URL; without an opener the CLI says to open it by hand | Low | Any | F | `-b`, `--browser` | `DeviceCode_Browser_OpensSignInPage` |
| SGN-DEV-05 | – | A sign-in declined on the device page ends with exit 44 and stores no session | Medium | Any | F | `account` | `DeviceCode_Declined_StoresNoSession` |
| SGN-DEV-06 | – | A user in several organizations without `-o` gets exit 2 and the list of organizations on redirected input | High | Any | F, X:MultiOrganization | `-o` | `DeviceCode_SeveralOrganizations_RequiresOrganization` |
| SGN-DEV-07 | – | `-o` with an organization the user is not a member of fails with exit 2 | Medium | Any | F | `--organization` | `DeviceCode_UnknownOrganization_Fails` |

- `SGN-DEV-01`: *Pre:* an empty `SFORM_HOME`, `SFORM_DEVICE_CODE_OUTPUT=json`. *Steps:* `account -o <org>`; read the JSON prompt on stderr; approve `verificationUriComplete` in the browser as the leased user. *Expect:* "You have been logged in as `<email>`", the account name and id, "You have access to N access points", "No default project is set" and exit 43. A second `account` run says "You are already logged in" and never prompts.
- `SGN-DEV-02`: *Steps:* `access`, then `login`. *Expect:* both print "You are already logged in as `<email>`" and the same account id as `account`.
- `SGN-DEV-03`: *Steps:* `account -o <org>`, read stdout until the code appears, approve it. *Expect:* the text has the verification URI, the user code, "with the code filled in" with the complete URI, and "The code expires in N minute(s)". No JSON line on stderr.
- `SGN-DEV-04`: *Pre:* (a) `SFORM_BROWSER` set to a recording helper program; (b) `SFORM_BROWSER` set to a program that does not exist. *Steps:* `account -o <org> -b` in (a), and `account -o <org> --browser` in (b), approving each code. *Expect:* (a) the helper received `verificationUriComplete` as its only argument; (b) "The browser could not be opened, please open the address manually." and the sign-in still succeeds.
- `SGN-DEV-05`: *Steps:* `account -o <org>`; decline the request on the device page. *Expect:* exit 44 and a message about the declined sign-in; a following `account` prompts for a new code.
- `SGN-DEV-06`: *Steps:* `account` without `-o`, with input redirected. *Expect:* exit 2 within the timeout (no hang). stderr lists both organizations with their ids and names `-o|--organization`.
- `SGN-DEV-07`: *Steps:* `account --organization <generated id>`, approve the code. *Expect:* exit 2, the organization is named in the error, and no session is stored.

### Machine Sign-in (`MachineSignInTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| SGN-MSI-01 | – | A machine with an API key signs in from the `STORYTELLER_*` variables without a prompt | Elementary | Machine | M | `account` | `Machine_ApiKey_SignsIn` |
| SGN-MSI-02 | – | `STORYTELLER_KEY` gives the machine its project and view, and `-p` wins over it | Medium | Machine | M | `account`, `-p` | `Machine_StorytellerKey_SetsTarget` |
| SGN-MSI-03 | – | Wrong machine credentials fail with exit 44 and name the variable | High | Machine | M | `account` | `Machine_WrongCredentials_Unauthorized` |

- `SGN-MSI-01`: *Pre:* `STORYTELLER_ENDPOINT` and the API key variable of the configured machine. *Steps:* `account`. *Expect:* exit 0, the machine's id and project in the output, and no file in `SFORM_HOME`.
- `SGN-MSI-02`: *Steps:* `story get <rst>` with `STORYTELLER_KEY=<org>.<project>.<view>.<rst>`; then with `-p <org>.<other>`. *Expect:* the first reads from the key's project. The second targets the other project, and is refused when the machine has no access to it.
- `SGN-MSI-03`: *Steps:* `account` with a modified API key. *Expect:* exit 44, and the message names the credential variable, not its value.

### Logout and Registration (`LogoutTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| SGN-OUT-01 | – | `account logout` removes the session, and the next command asks for a sign-in | High | Any | F | `logout` | `Logout_SignedIn_RemovesSession` |
| SGN-OUT-02 | – | `account logout` without a session or with machine credentials changes nothing and exits 0 | Low | Any | F, M | `logout` | `Logout_NoSessionOrMachine_NothingToDo` |
| SGN-OUT-03 | – | `account register` for a registered user says so and exits 0; without a sign-in it asks for `sform login` | Medium | Any | F | `register` | `Register_RegisteredOrSignedOut_Reports` |
| SGN-OUT-04 | – | `account register` creates the account, the first project and the default project of a new user | Low | Any | F, T, X:Registration | `register` | `Register_NewUser_CreatesAccountAndProject` |

- `SGN-OUT-01`: *Steps:* sign in as in SGN-DEV-01, `account logout`, then `story -p <org>.<project>`. *Expect:* logout prints the user and exits 0; the token store in `SFORM_HOME` is empty; the read fails with exit 44 and asks for `sform login`.
- `SGN-OUT-02`: *Steps:* `account logout` in an empty `SFORM_HOME`; then with machine variables. *Expect:* "No account is logged in this CLI instance."; then "sform uses the machine credentials in `STORYTELLER_…`". Both exit 0.
- `SGN-OUT-03`: *Steps:* `account register` as the signed-in user; then in an empty `SFORM_HOME`. *Expect:* "You are already registered as `<email>` #`<id>`" and exit 0; then "You are not logged in, please call sform login first." and exit 43.
- `SGN-OUT-04`: *Pre:* an unregistered pool user, signed in. *Steps:* `account register` in a terminal; answer the organization and project prompts with generated names. *Expect:* "Account … has been created", the default project set to `<org>.<project>` with the default view, and `access.default.json` written.

## Account and Access Points

### Default Project (`AccountSetTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| ACC-SET-01 | – | `account set -p -v` stores the default project and view, and later commands use them | Elementary | Any | | `set`, `-p`, `--projectKey`, `-v`, `--viewName` | `AccountSet_ProjectAndView_BecomeDefault` |
| ACC-SET-02 | – | `account set` in a terminal offers the user's projects and stores the choice | Low | Any | T | `set` | `AccountSet_Interactive_SelectsProject` |
| ACC-SET-03 | – | `account set` without `-p` on redirected input fails with exit 2 | Medium | Any | | `set` | `AccountSet_RedirectedWithoutProject_Fails` |
| ACC-SET-04 | – | `account set -p` with a project the user has no access to fails and keeps the old default | Medium | Any | | `-p` | `AccountSet_UnknownProject_KeepsDefault` |

- `ACC-SET-01`: *Steps:* `account set -p <org>.<project> -v <view>`; `account`; `story` (no `-p`); then `account set --projectKey <org>.<project> --viewName default`. *Expect:* `account` prints "Default project set to `<org>.<project>` with `<view>` view" and exit 0. `story` lists the annotations of that view. The second set switches to `default`.
- `ACC-SET-02`: *Steps:* `account set` in a terminal; pick the target project from the list. *Expect:* the list shows the user's projects with the current default preselected, and the choice is stored.
- `ACC-SET-03`: *Steps:* `account set` with input redirected. *Expect:* exit 2 within the timeout; stderr names `-p`.
- `ACC-SET-04`: *Steps:* `account set -p <org>.<generated>`. *Expect:* a non-zero exit with the project named; `access.default.json` unchanged.

### Access Points (`AccessPointTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| ACC-PNT-01 | – | `account points` lists the user's access points with the roles | High | Any | | `points` | `Points_List_ShowsRoles` |
| ACC-PNT-02 | – | `points get -p` shows the members and roles of a project to an Administrator | High | Admin | | `get`, `-p`, `--pointKey` | `Points_Get_ShowsMembers` |
| ACC-PNT-03 | – | `points get` by a Contributor is refused with exit 44 | Medium | Contributor | | `get` | `Points_GetAsContributor_Unauthorized` |
| ACC-PNT-04 | – | `points create` creates a project the creator owns | Medium | Any | T, P | `create` | `Points_Create_CreatesOwnedProject` |
| ACC-PNT-05 | – | `points grant` gives each role, Reader to Owner, to another pool user | High | Owner | P | `grant`, `arg0`, `-p`, `--point`, `-r`, `--role` | `Points_Grant_EachRole` |
| ACC-PNT-06 | – | `points revoke -r` removes one role, and `revoke` without `-r` removes all | High | Owner | P | `revoke`, `arg0`, `-p`, `--point`, `-r`, `--role` | `Points_Revoke_OneOrAllRoles` |
| ACC-PNT-07 | – | An Administrator cannot grant Owner, and an unknown role fails with exit 2 | Medium | Admin | P | `grant`, `-r` | `Points_GrantOwnerAsAdmin_Refused` |

- `ACC-PNT-01`: *Steps:* `account points`. *Expect:* the target project with the user's role; exit 0.
- `ACC-PNT-02`: *Steps:* `account points get -p <org>.<project>`; `account points get --pointKey <org>`. *Expect:* each lists the members with their roles, the leased user among them.
- `ACC-PNT-03`: *Steps:* `account points get -p <org>.<contributor project>`. *Expect:* exit 44.
- `ACC-PNT-04`: *Steps:* `account points create` in a terminal; enter a generated organization and project (or a project in the disposable organization). *Expect:* the project exists, and `points get` lists the user as Owner.
- `ACC-PNT-05`: *Pre:* the account id of a second pool user (from `account` as that user). *Steps:* `points grant <id> -p <org>.<project> -r Reader`, then `--point … --role Contributor`, `Administrator`, `Owner`. *Expect:* after each, `points get` shows the granted role.
- `ACC-PNT-06`: *Steps:* grant Contributor and Administrator, `points revoke <id> -p … -r Administrator`, then `points revoke <id> --point …`. *Expect:* first only Contributor remains, then the user is not a member.
- `ACC-PNT-07`: *Steps:* as an Administrator, `points grant <id> -p … -r Owner`; then `-r Superuser`. *Expect:* the first is refused (exit 44); the second fails with exit 2 and lists the roles.

### Certificate Authority and Assistance (`AccountToolsTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| ACC-TLS-01 | – | `account ca` prints the CA certificate as PEM, and `-o` writes it to a file | Medium | Any | | `ca`, `-o`, `--output` | `Ca_Download_WritesPem` |
| ACC-TLS-02 | – | `assist key` builds a valid annotation key from the answers | Low | Any | T | `assist`, `assistance`, `key` | `AssistKey_Answers_PrintsKey` |
| ACC-TLS-03 | – | `assist key` on redirected input fails fast instead of waiting | Low | Any | | `key` | `AssistKey_Redirected_FailsFast` |
| ACC-TLS-04 | – | `supervisor` and `scheduler` (and `supervise`, `schedule`) print that they are not available yet | Low | Any | | `supervisor`, `supervise`, `scheduler`, `schedule` | `Placeholders_PrintNotAvailable` |

- `ACC-TLS-01`: *Steps:* `account ca -p <org>.<project>`; `account ca -p … -o ca.pem`; `account ca -p … --output out/ca.pem`. *Expect:* a PEM block on stdout, then the same certificate in each file (it parses as X.509 and has the CA flag).
- `ACC-TLS-02`: *Steps:* `assist key` in a terminal; pick Unit; answer `my-app` and `nightly`. Then `assistance key`, picking Responsibility. *Expect:* the printed key is `unt.my-app.nightly` (and a responsibility key), which `story set` accepts.
- `ACC-TLS-03`: *Steps:* `assist key` with input redirected. *Expect:* a non-zero exit within the timeout, naming the interactive prompt.
- `ACC-TLS-04`: *Steps:* each of the four names. *Expect:* "Under the development, will be implemented in version 1.1" and exit 0.

## Machines

### Machine Lifecycle (`MachineLifecycleTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| MCH-LIF-01 | – | `account machine` lists the project's machines with kind and scopes | High | Editor | | `machine` | `Machines_List_ShowsKindAndScopes` |
| MCH-LIF-02 | – | `account agent` is the same list, and a Reader is refused | Medium | Reader | | `agent` | `Machines_ListAsReader_Unauthorized` |
| MCH-LIF-03 | – | A created API key machine signs in with its key and reads with the default read-only scopes | Elementary | Editor | | `create`, `-r` | `Machine_CreateApiKey_ReadsOnly` |
| MCH-LIF-04 | – | `-w` gives read and write scopes, and the machine writes a configuration | High | Editor | | `-w`, `--write` | `Machine_CreateWrite_WritesConfiguration` |
| MCH-LIF-05 | – | `-r` read-only is refused a write with exit 44 | High | Editor | | `--read-only` | `Machine_ReadOnly_WriteRefused` |
| MCH-LIF-06 | – | `-a` restricts the machine to one annotation and its descendants | High | Editor | | `-a`, `--annotation` | `Machine_Annotation_RestrictsAccess` |
| MCH-LIF-07 | – | `--scope` accepts the enum name, the scope string and kebab case, repeated or comma-separated | Medium | Secrets | | `--scope` | `Machine_ScopeSyntaxes_Accepted` |
| MCH-LIF-08 | – | A `Secrets.*` scope needs ContributorWithSecrets: a Contributor is refused | High | Contributor | | `--scope` | `Machine_SecretsScopeAsContributor_Refused` |
| MCH-LIF-09 | – | `-m` grants each management scope, with an Administrator only | High | Admin | | `-m`, `--manage` | `Machine_ManagementScopes_Granted` |
| MCH-LIF-10 | – | `-m` by a ContributorWithSecrets is refused | Medium | Secrets | | `-m` | `Machine_ManageAsNonAdmin_Refused` |
| MCH-LIF-11 | – | `machine set` is an alias of `create` | Low | Editor | | `set` | `Machine_SetAlias_Creates` |
| MCH-LIF-12 | – | `machine reset` issues a new API key, and the old one stops working | High | Editor | | `reset`, `arg0` | `Machine_Reset_RotatesKey` |
| MCH-LIF-13 | – | `machine delete` (and `remove`) removes the machine, and its key stops working | High | Editor | | `delete`, `remove`, `arg0` | `Machine_Delete_RevokesAccess` |

- `MCH-LIF-01`: *Steps:* create a machine, `account machine -p <org>.<project>`. *Expect:* the machine with its id, kind ApiKey and scopes.
- `MCH-LIF-02`: *Steps:* `account agent -p …` as an Editor, then as a Reader. *Expect:* the same list; the Reader gets exit 44.
- `MCH-LIF-03`: *Steps:* `machine create -p … -r`; capture the id and the key; `account` and `story get <rst>` with the key in the variables. *Expect:* scopes AnnotationRead and ConfigurationRead; the machine signs in and reads.
- `MCH-LIF-04`: *Steps:* `machine create -p … -w` (and `--write`); `config set <rst> -x a=1` as the machine. *Expect:* AnnotationRead/Write and ConfigurationRead/Write; the write succeeds.
- `MCH-LIF-05`: *Steps:* `machine create -p … --read-only`; `config set <rst> -x a=1` as the machine. *Expect:* exit 44 naming Configuration.Write.
- `MCH-LIF-06`: *Steps:* `machine create -p … -w -a <rst>`, then `--annotation <rst>`; as the machine, read `<rst>.<unt>` and another responsibility. *Expect:* the descendant reads; the other is refused (exit 44).
- `MCH-LIF-07`: *Steps:* `machine create -p … --scope SecretsRead --scope Secrets.Write`; another with `--scope secrets-read,configuration-read`. *Expect:* the stored scopes equal the requested ones plus the read scopes the server adds.
- `MCH-LIF-08`: *Steps:* `machine create -p <contributor project> --scope SecretsRead`. *Expect:* exit 44.
- `MCH-LIF-09`: *Steps:* `machine create -p … -w -m ConfigurationTransfer,SchemaWrite -m TemplateWrite --manage AnnotationManage --manage KeyValueManage`. *Expect:* all five management scopes are stored, next to the runtime scopes.
- `MCH-LIF-10`: *Steps:* `machine create -p … -m SchemaWrite` as a ContributorWithSecrets. *Expect:* exit 44.
- `MCH-LIF-11`: *Steps:* `machine set -p … -r`. *Expect:* a machine is created as by `create`.
- `MCH-LIF-12`: *Steps:* create; `machine reset <id>`; sign in with the old key, then with the new one. *Expect:* old key exit 44, new key exit 0.
- `MCH-LIF-13`: *Steps:* create two; `machine delete <id1>`; `machine remove <id2>`. *Expect:* neither is listed, and both keys get exit 44.

### Credential Kinds (`MachineCredentialTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| MCH-CRD-01 | – | `-k Certificate` with `-o` writes a .pfx, and the machine signs in over mTLS | High | Editor | | `-k`, `--credential-kind`, `-o`, `--output`, `-l`, `--lifetime-days` | `Machine_Certificate_SignsInWithPfx` |
| MCH-CRD-02 | – | `-k CertificateAndApiKey` needs both, and `-s` uses a shared certificate with the API key | Medium | Editor | | `-k`, `-s`, `--shared-certificate` | `Machine_CertificateAndApiKey_SharedCertificate` |
| MCH-CRD-03 | – | `-k ClientCredentials` returns a client id and secret that sign in | Medium | Editor | | `-k` | `Machine_ClientCredentials_SignsIn` |
| MCH-CRD-04 | – | `-k ApiKey` explicitly, and a kind the project does not enable fails with exit 2 | Medium | Editor | P | `-k` | `Machine_DisabledKind_Fails` |
| MCH-CRD-05 | – | `machine renew -c -o` renews the certificate of a certificate machine | High | Editor | | `renew`, `arg0`, `-c`, `--certificate`, `-o`, `--output` | `Machine_Renew_WritesNewPfx` |

- `MCH-CRD-01`: *Steps:* `machine create -p … -r -k Certificate -l 7 -o m.pfx`; a second with `--credential-kind Certificate --lifetime-days 30 --output m2.pfx`; sign in with the certificate variables. *Expect:* the .pfx files exist, with expiry in 7 and 30 days; the machine signs in.
- `MCH-CRD-02`: *Pre:* a shared certificate (CRT-SHC-02). *Steps:* `machine create -p … -k CertificateAndApiKey -o m.pfx`; then `-k CertificateAndApiKey -s`; sign in with the shared .pfx and each key. *Expect:* the first has its own certificate; the `-s` machine has no .pfx and signs in with the shared certificate and its key.
- `MCH-CRD-03`: *Steps:* `machine create -p … -r -k ClientCredentials`; sign in with the client id and secret variables. *Expect:* the client credentials are printed once and work.
- `MCH-CRD-04`: *Pre:* `machine-auth -k ApiKey` on the disposable project. *Steps:* `machine create -p … -k ApiKey`; then `-k Certificate`. *Expect:* the first works; the second fails with exit 2 and lists the enabled kinds.
- `MCH-CRD-05`: *Steps:* create a certificate machine; `machine renew <id> -c m.pfx -o renewed.pfx`; then `--certificate renewed.pfx --output r2.pfx`. *Expect:* each new .pfx has a later expiry and signs in; the old certificate stops working (or the documented overlap applies).

### Authentication Policy (`MachineAuthPolicyTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| MCH-POL-01 | – | `machine machine-auth` shows the project's policy | Medium | Editor | | `machine-auth` | `MachineAuth_Show_PrintsPolicy` |
| MCH-POL-02 | – | `machine-auth -k -d -l` sets the kinds, the default kind and the lifetime | High | Admin | P | `-k`, `--credential-kind`, `-d`, `--default-kind`, `-l`, `--lifetime-days` | `MachineAuth_Set_ChangesPolicy` |
| MCH-POL-03 | – | A default kind that is not enabled fails with exit 2 | Low | Admin | P | `-d` | `MachineAuth_DefaultNotEnabled_Fails` |

- `MCH-POL-01`: *Steps:* `account machine machine-auth -p …`. *Expect:* the enabled kinds, the default kind and the lifetime.
- `MCH-POL-02`: *Steps:* `machine-auth -p … -k ApiKey -k Certificate -d Certificate -l 14`; then `--credential-kind ApiKey,ClientCredentials --default-kind ApiKey --lifetime-days 30`; create a machine without `-k`. *Expect:* the policy shows each change, and the new machine gets the default kind.
- `MCH-POL-03`: *Steps:* `machine-auth -p … -k ApiKey -d Certificate`. *Expect:* exit 2, the policy unchanged.

### Federation (`MachineFederationTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| MCH-FED-01 | – | `machine issuer set` with `--jwks-file` stores an issuer, and `issuer list` shows it | High | Admin | P | `issuer`, `issuers`, `set`, `list`, `arg0`, `--issuer`, `--jwks-file`, `--max-lifetime`, `--require-claim`, `--description` | `Issuer_SetWithJwksFile_Listed` |
| MCH-FED-02 | – | `issuer create --jwks-uri` replaces settings not given, and the issuer URL of an existing issuer cannot change | Medium | Admin | P | `create`, `--jwks-uri` | `Issuer_Replace_ClearsSettings` |
| MCH-FED-03 | – | `issuer list` works for a Contributor; `issuer set` by a Contributor is refused | Medium | Contributor | | `list`, `set` | `Issuer_ContributorRights` |
| MCH-FED-04 | – | `issuer delete` (and `remove`) deletes an issuer, even with rules referencing it | Medium | Admin | P | `delete`, `remove`, `arg0` | `Issuer_Delete_Removes` |
| MCH-FED-05 | – | A federated machine created with `--issuer --audience --subject --claim` exchanges a token the test signs | High | Admin | P | `--issuer`, `--audience`, `--subject`, `--claim` | `Machine_Federated_ExchangesToken` |
| MCH-FED-06 | – | A federated machine from `--rules-file` matches one of several rules | Medium | Admin | P | `--rules-file` | `Machine_FederatedRulesFile_Matches` |
| MCH-FED-07 | – | `machine rules` shows the rules, and replaces them with options or a file | High | Admin | P | `rules`, `arg0`, `--issuer`, `--audience`, `--subject`, `--claim`, `--rules-file` | `MachineRules_ShowAndReplace` |
| MCH-FED-08 | – | A token with a wrong subject, a missing claim or a lifetime above `--max-lifetime` is refused | Medium | Admin | P | `--subject`, `--require-claim`, `--max-lifetime` | `Machine_FederatedWrongToken_Refused` |

- `MCH-FED-01`: *Pre:* the test generates an RSA key pair and a JWKS file. *Steps:* `machine issuer set ci --issuer https://issuer.test/ --jwks-file jwks.json --max-lifetime 600 --require-claim repo=a --require-claim repo=b --description "CLI tests"`; `machine issuers list`. *Expect:* the issuer with its settings; the alternatives of `repo` listed.
- `MCH-FED-02`: *Steps:* `issuer create ci --issuer https://issuer.test/ --jwks-uri https://…/jwks`; then the same name with another `--issuer`. *Expect:* the description and the claims are cleared; the change of the issuer URL fails with exit 2.
- `MCH-FED-03`: *Steps:* as a Contributor, `issuer list`, then `issuer set x --issuer …`. *Expect:* the list works; the set gets exit 44.
- `MCH-FED-04`: *Steps:* create two issuers and a rule on one; `issuer delete <a>`, `issuer remove <b>`. *Expect:* both are gone from the list.
- `MCH-FED-05`: *Steps:* `machine create -p … -r --issuer ci --audience <token endpoint> --subject repo:x:* --claim env=prod`; sign a JWT with the matching claims; sign in as the machine with the federated token variables. *Expect:* kind Federated (implied by the rule); the exchange succeeds and the machine reads.
- `MCH-FED-06`: *Steps:* write a rules file with two rules; `machine create -p … -r --rules-file rules.json`; sign in with tokens for each rule. *Expect:* both are accepted.
- `MCH-FED-07`: *Steps:* `machine rules <id>`; `machine rules <id> --issuer ci --audience … --subject sub-2 --claim k=v`; `machine rules <id> --rules-file rules.json`. *Expect:* each call shows the current rules, and the token of an old rule is refused after a replace.
- `MCH-FED-08`: *Steps:* exchange tokens with a wrong `sub`, without the required claim, and with `exp - iat` above 600 seconds. *Expect:* each sign-in fails with exit 44.

## Shared Certificates

### Shared Certificates (`SharedCertificateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| CRT-SHC-01 | – | `account shared-cert` (and `certificate`) lists the project's shared certificates | Medium | Editor | | `shared-cert`, `certificate` | `SharedCert_List_ShowsCertificates` |
| CRT-SHC-02 | – | `shared-cert issue -l -o` writes a .pfx with the label and the lifetime | High | Admin | P | `issue`, `arg0`, `-l`, `--lifetime-days`, `-o`, `--output` | `SharedCert_Issue_WritesPfx` |
| CRT-SHC-03 | – | `shared-cert create` is an alias of `issue` | Low | Admin | P | `create` | `SharedCert_CreateAlias_Issues` |
| CRT-SHC-04 | – | `shared-cert revoke` (and `delete`) by thumbprint stops the certificate | High | Admin | P | `revoke`, `delete`, `arg0` | `SharedCert_Revoke_StopsSignIn` |
| CRT-SHC-05 | – | Issuing a shared certificate below the needed role is refused | Medium | Contributor | | `issue` | `SharedCert_IssueAsContributor_Refused` |

- `CRT-SHC-01`: *Steps:* issue one; `account shared-cert -p …`; `account certificate -p …`. *Expect:* both list it with its label, thumbprint and expiry.
- `CRT-SHC-02`: *Steps:* `shared-cert issue lbl-1 -p … -l 10 -o s.pfx`; `issue lbl-2 --lifetime-days 20 --output s2.pfx`. *Expect:* two .pfx files with those lifetimes; the list shows both labels.
- `CRT-SHC-03`: *Steps:* `shared-cert create lbl-3 -o s3.pfx`. *Expect:* as issue.
- `CRT-SHC-04`: *Steps:* `shared-cert revoke <thumbprint 1>`; `shared-cert delete <thumbprint 2>`; sign in a `-s` machine with each. *Expect:* both revoked; the sign-ins fail with exit 44.
- `CRT-SHC-05`: *Steps:* `shared-cert issue x -p <contributor project>`. *Expect:* exit 44.

## Annotations

### List and Query (`AnnotationListTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| ANN-LST-01 | – | `storyteller` (and `story`, `annotations`) lists the annotations of the view | High | Reader | | `storyteller`, `story`, `annotations` | `Story_List_ShowsAnnotations` |
| ANN-LST-02 | – | `-r` and `-s` query responsibilities and subjects by name | Medium | Reader | | `-r`, `--responsibilities`, `-s`, `--subjects` | `Story_QueryByName_Filters` |
| ANN-LST-03 | – | `-u` adds the usages of the queried responsibilities or subjects | Medium | Reader | | `-u`, `--usages` | `Story_Usages_Combined` |
| ANN-LST-04 | – | `-c` queries contexts, and `-e` adds executions | Medium | Reader | | `-c`, `--contexts`, `-e`, `--executions` | `Story_ContextsAndExecutions` |
| ANN-LST-05 | – | `-d` lists the descendants of one annotation | Medium | Reader | | `-d`, `--descendants` | `Story_Descendants_Listed` |
| ANN-LST-06 | – | `-t` renders a tree by subjects, and `-b` picks the tree's root type | Low | Reader | | `-t`, `--tree`, `-b`, `--tree-root` | `Story_Tree_Rendered` |
| ANN-LST-07 | – | `-k` continues a long list with the printed continuation token | Medium | Reader | P | `-k`, `--continuation-token` | `Story_ContinuationToken_NextPage` |
| ANN-LST-08 | – | `-i`, `--deleted` and `-a` include inactive, deleted, or all annotations | Medium | Reader | | `-i`, `--inactive`, `--deleted`, `-a`, `--all` | `Story_IncludeInactiveDeletedAll` |

- `ANN-LST-01`: *Pre:* `<rst>` with a unit. *Steps:* `storyteller -p …`, `story -p …`, `annotations -p …`. *Expect:* the same list, `<rst>` in it.
- `ANN-LST-02`: *Pre:* generated responsibilities and subjects with a common name part. *Steps:* `story -r <name>`, `--responsibilities <name>`, `-s <name>`, `--subjects <name>`. *Expect:* only the matching annotations of the type.
- `ANN-LST-03`: *Pre:* a usage below a subject. *Steps:* `story -s <name> -u`; `story -r <name> --usages`. *Expect:* the usages appear with their owners.
- `ANN-LST-04`: *Pre:* a context and an execution. *Steps:* `story -c <name>`, `--contexts <name> -e`, `-r <name> --executions`. *Expect:* contexts, then their executions.
- `ANN-LST-05`: *Steps:* `story -d <rst>`, `--descendants <rst>`. *Expect:* the descendants of `<rst>` only.
- `ANN-LST-06`: *Steps:* `story -t`, `story --tree -b sbt`, `--tree-root rst`. *Expect:* an indented tree rooted at the type.
- `ANN-LST-07`: *Pre:* more annotations than a page in the disposable project. *Steps:* `story`; then `story -k <token>`; `--continuation-token <token 2>`. *Expect:* each page continues the previous, with no repeated keys.
- `ANN-LST-08`: *Pre:* a disabled, a scheduled and a deleted annotation. *Steps:* `story`, `story -i`, `--inactive`, `--deleted`, `-a`, `--all`. *Expect:* the default list has none of them; `-i` adds disabled and scheduled; `--deleted` adds deleted; `-a` has all three.

### Get and Set (`AnnotationEditTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| ANN-SET-01 | – | `story set` creates an annotation, and `story get` returns it | Elementary | Editor | | `set`, `arg0`, `get`, `arg0` | `Story_SetGet_RoundTrip` |
| ANN-SET-02 | – | `story create` is an alias of `set`, and a second set updates | Medium | Editor | | `create` | `Story_CreateAlias_Updates` |
| ANN-SET-03 | – | `-c` sets custom properties with typed values, and `-l` sets labels | High | Editor | | `-c`, `--custom-properties`, `-l`, `--labels` | `Story_CustomPropertiesAndLabels` |
| ANN-SET-04 | – | `-i` imports one or several annotations from a file | High | Editor | | `-i`, `--import` | `Story_Import_FromFile` |
| ANN-SET-05 | – | `get -e` saves the annotation into a file | Medium | Reader | | `-e`, `--export` | `Story_GetExport_WritesFile` |
| ANN-SET-06 | – | Setting a deleted annotation fails without an option, `--restore-deleted` restores it, `--recreate` creates it new | High | Editor | | `--restore-deleted`, `--recreate` | `Story_SetDeleted_RestoreOrRecreate` |
| ANN-SET-07 | – | An invalid key fails with exit 2, an unknown key with a not-found error | Medium | Reader | | `arg0` | `Story_InvalidOrUnknownKey_Fails` |
| ANN-SET-08 | – | A Reader's `story set` is refused with exit 44 | Elementary | Reader | | `set` | `Story_SetAsReader_Unauthorized` |
| ANN-SET-09 | – | A machine with Annotation.Write sets, and without it is refused | Medium | Machine | M | `set` | `Story_MachineScopes_Decide` |

- `ANN-SET-01`: *Steps:* `story set <rst>`; `story get <rst>`. *Expect:* exit 0 both; get shows the key, the type and the name.
- `ANN-SET-02`: *Steps:* `story create <rst>`; `story set <rst> -l a`. *Expect:* one annotation, updated.
- `ANN-SET-03`: *Steps:* `story set <rst> -c owner=team-a -c count=3 -c on=true -c since=2026-11-01 --custom-properties ttl=1:30:00 -l x -l y --labels z`. *Expect:* get shows the properties as string, number, boolean, date and duration, and the labels x, y, z.
- `ANN-SET-04`: *Steps:* write a file with three annotations; `story set -i anns.json`, then a single one with `--import one.json`. *Expect:* all are created as in the file.
- `ANN-SET-05`: *Steps:* `story get <rst> -e a.json`; `--export out/b.json`. *Expect:* the files hold the annotation JSON.
- `ANN-SET-06`: *Pre:* `<rst>` with a configuration, deleted. *Steps:* `story set <rst>`; `story set <rst> --restore-deleted -l r`; delete again; `story set <rst> --recreate`. *Expect:* the first fails and names both options; the restore brings back the configuration and history with the label; the recreate has an empty configuration.
- `ANN-SET-07`: *Steps:* `story get not-a-key`; `story get rst.<generated>`. *Expect:* exit 2 for the first; a not-found message and a non-zero exit for the second.
- `ANN-SET-08`: *Steps:* `story set <rst>` in the Reader project. *Expect:* exit 44.
- `ANN-SET-09`: *Steps:* as a `-w` machine, then a `-r` machine, `story set <rst>`. *Expect:* success, then exit 44 naming Annotation.Write.

### Delete, Restore and History (`AnnotationDeleteTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| ANN-DEL-01 | – | `story delete` soft-deletes an annotation with its descendants | High | Editor | | `delete`, `arg0` | `Story_Delete_SoftDeletesTree` |
| ANN-DEL-02 | – | `story remove` is an alias of `delete` | Low | Editor | | `remove` | `Story_RemoveAlias_Deletes` |
| ANN-DEL-03 | – | `--permanent -y` deletes for good as an Administrator | High | Admin | | `--permanent`, `-y`, `--yes` | `Story_PermanentDelete_Admin` |
| ANN-DEL-04 | – | `--permanent` asks for a confirmation in a terminal; No aborts with exit 40 | Medium | Admin | T | `--permanent` | `Story_PermanentDelete_ConfirmOrAbort` |
| ANN-DEL-05 | – | `--permanent` without `-y` on redirected input fails fast, and by a Contributor is refused | Medium | Contributor | | `--permanent` | `Story_PermanentDelete_RedirectedOrContributor` |
| ANN-DEL-06 | – | `story restore` restores several keys with their descendants | High | Editor | | `restore`, `arg0` | `Story_Restore_WithDescendants` |
| ANN-DEL-07 | – | `restore --without-descendants` keeps the descendants deleted | Medium | Editor | | `--without-descendants` | `Story_RestoreWithoutDescendants` |
| ANN-DEL-08 | – | `story deleted` (and `trash`) lists the deleted keys; `-a` lists every deleted annotation, `-k` pages | Medium | Reader | P | `deleted`, `trash`, `-a`, `--all`, `-k`, `--continuation-token` | `Story_Deleted_ListsTrash` |
| ANN-DEL-09 | – | `story history` shows who changed what; `-n` limits it and `--json` writes JSON | Medium | Reader | | `history`, `arg0`, `-n`, `--top`, `--json` | `Story_History_ShowsChanges` |

- `ANN-DEL-01`: *Steps:* `story delete <rst>`. *Expect:* `<rst>` and its unit are gone from `story` and appear in `story deleted`.
- `ANN-DEL-02`: *Steps:* `story remove <rst2>`. *Expect:* as delete.
- `ANN-DEL-03`: *Steps:* `story delete <rst> --permanent -y`; another with `--yes`. *Expect:* not listed in `deleted`; `restore` fails.
- `ANN-DEL-04`: *Steps:* `story delete <rst> --permanent` in a terminal; answer No, then Yes. *Expect:* exit 40 and the annotation still there; then deleted.
- `ANN-DEL-05`: *Steps:* `story delete <rst> --permanent` with input redirected; then as a Contributor with `-y`. *Expect:* a fast non-zero exit naming `--yes`; exit 44.
- `ANN-DEL-06`: *Steps:* delete `<rst1>` and `<rst2>`; `story restore <rst1> <rst2>`. *Expect:* both, with their units, are back.
- `ANN-DEL-07`: *Steps:* delete `<rst>`; `story restore <rst> --without-descendants`. *Expect:* `<rst>` is back, its unit is still deleted.
- `ANN-DEL-08`: *Pre:* a deleted parent with deleted children; more deleted keys than a page. *Steps:* `story deleted`, `story trash`, `story deleted -a`, `--all`, `-k <token>`, `--continuation-token <token>`. *Expect:* the default shows the requested keys, newest first; `-a` adds the children; the pages continue.
- `ANN-DEL-09`: *Pre:* `<rst>` changed three times. *Steps:* `story history <rst>`, `-n 1`, `--top 2`, `--json`. *Expect:* entries with the user and time, newest first; 1 and 2 entries; valid JSON with the same entries.

### Export and Import (`AnnotationTransferTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| ANN-TRN-01 | – | `story export` saves the view as a folder zip with the server's file name | High | Admin | P | `export` | `Export_Default_SavesZip` |
| ANN-TRN-02 | – | `-f git` exports a git repository with every version as a commit | Medium | Admin | P | `-f`, `--format` | `Export_Git_HasCommits` |
| ANN-TRN-03 | – | `-i` limits the kinds, comma-separated or repeated | Medium | Admin | P | `-i`, `--include` | `Export_IncludeKinds_Limits` |
| ANN-TRN-04 | – | `-o` saves to a file or a folder, and `-x` unpacks into an empty folder | Medium | Admin | P | `-o`, `--output`, `-x`, `--extract` | `Export_OutputAndExtract` |
| ANN-TRN-05 | – | `--timeout` stops waiting while the export continues on the server | Low | Admin | P | `--timeout` | `Export_Timeout_StopsWaiting` |
| ANN-TRN-06 | – | `story import` imports a zip into another view, all four kinds | High | Admin | P | `import`, `arg0` | `Import_Zip_ImportsView` |
| ANN-TRN-07 | – | `import` of a folder packs its JSON files without hidden files | Medium | Admin | P | `arg0` | `Import_Folder_SkipsHidden` |
| ANN-TRN-08 | – | `-e skip` keeps existing documents, `-e overwrite` writes new versions | Medium | Admin | P | `-e`, `--existing` | `Import_ExistingMode_SkipOrOverwrite` |
| ANN-TRN-09 | – | `--dry-run` reports every outcome and writes nothing | Medium | Admin | P | `--dry-run` | `Import_DryRun_WritesNothing` |
| ANN-TRN-10 | – | Without `--force` a breaking schema fails and the rest imports; `--force` stores it with a warning | Medium | Admin | P | `--force`, `-i`, `--include`, `--timeout` | `Import_Force_StoresBreaking` |
| ANN-TRN-11 | – | Export and import by a Contributor are refused; a machine needs Configuration.Transfer | Medium | Contributor | M | `export`, `import` | `Transfer_RolesAndScopes` |

- `ANN-TRN-01`: *Pre:* a project with annotations, a schema, a template and configurations. *Steps:* `story export -p …`. *Expect:* a zip in the working folder with the server's name; it holds JSON files of the four kinds with versions.
- `ANN-TRN-02`: *Steps:* `story export -f git`, `--format folder`. *Expect:* the git zip has a repository whose log has a commit per version; the folder zip as in 01.
- `ANN-TRN-03`: *Steps:* `story export -i annotations,schemas`; `--include templates --include configurations`. *Expect:* only those kinds in each zip.
- `ANN-TRN-04`: *Steps:* `export -o out.zip`; `--output dir/`; `-x -o unpacked`; `--extract --output unpacked2`. *Expect:* the zip at the path, in the folder, and the unpacked files.
- `ANN-TRN-05`: *Pre:* a project big enough to take longer than the shortest timeout. *Steps:* `story export --timeout 0`. *Expect:* the CLI stops waiting with a message naming the job, and the job ends on the server.
- `ANN-TRN-06`: *Steps:* export view A; `story import a.zip -p … -v B`. *Expect:* view B has the documents of A; the report lists the created documents.
- `ANN-TRN-07`: *Steps:* a folder with JSON files plus `.git/` and `._x.json`; `story import folder/`. *Expect:* only the visible JSON files are imported.
- `ANN-TRN-08`: *Steps:* change a configuration in B; `import a.zip -e skip`; `--existing overwrite`. *Expect:* skip keeps the change; overwrite writes A's content as a new version.
- `ANN-TRN-09`: *Steps:* `import a.zip --dry-run`. *Expect:* every outcome and warning reported; `story`, `config versions` unchanged.
- `ANN-TRN-10`: *Pre:* a zip with a schema the target's configurations break. *Steps:* `import x.zip -i schemas,configurations --timeout 10`; `import x.zip --include schemas --force`. *Expect:* the schema fails, and the configurations are imported; then the schema is stored with a warning.
- `ANN-TRN-11`: *Steps:* `story export` and `story import` as a Contributor; as a machine with and without `-m ConfigurationTransfer` (plus the read scopes). *Expect:* exit 44 for the Contributor and the machine without the scope; the machine with it exports.

## Configurations

### Get and Formats (`ConfigGetTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| CFG-GET-01 | – | `story config` (and `configuration`) returns the configuration model | Elementary | Reader | | `config`, `configuration`, `arg0` | `Config_Get_ReturnsModel` |
| CFG-GET-02 | – | `-r` returns the resolved configuration with bindings and inheritance applied | Elementary | Reader | | `-r`, `--resolved` | `Config_Resolved_AppliesBindings` |
| CFG-GET-03 | – | `-f` prints the content as flat, properties, env, yaml and json | High | Reader | | `-f`, `--format` | `Config_Formats_RenderContent` |
| CFG-GET-04 | – | `--content-only` prints the content alone, and messages go to stderr | Medium | Reader | | `--content-only` | `Config_ContentOnly_PipesContent` |
| CFG-GET-05 | – | `-e` saves the model, or the formatted content with `-f`, into a file | Medium | Reader | | `-e`, `--export` | `Config_Export_WritesFile` |
| CFG-GET-06 | – | `--version` returns the stored content of an old version | Medium | Reader | | `--version` | `Config_Version_ReturnsOld` |
| CFG-GET-07 | – | `--env-prefix` prefixes every env name, and an invalid prefix fails with exit 2 | Medium | Reader | | `--env-prefix` | `Config_EnvPrefix_Prefixes` |
| CFG-GET-08 | – | A resolved configuration with a secret binding needs ContributorWithSecrets | High | Contributor | | `-r` | `Config_ResolvedSecretAsContributor_Refused` |

- `CFG-GET-01`: *Pre:* `<rst>` with a configuration. *Steps:* `story config <rst>`; `story configuration <rst>`. *Expect:* the model with the content, version and labels.
- `CFG-GET-02`: *Pre:* a parent configuration and a binding to a public key-value. *Steps:* `config <rst>.<unt> -r`, `--resolved`. *Expect:* the inherited values merged and the binding replaced by the value.
- `CFG-GET-03`: *Steps:* `config <rst> -f flat`, `--format properties`, `-f env`, `-f yaml`, `-f json`. *Expect:* each output parses in its format and has the same values (per `docs/Platform/Storyteller/configuration-formats.md`).
- `CFG-GET-04`: *Steps:* `config <rst> --content-only`; `config <rst> -f yaml --content-only`. *Expect:* stdout is the content JSON alone (no model), then the YAML alone; nothing else on stdout.
- `CFG-GET-05`: *Steps:* `config <rst> -e m.json`; `--export c.env -f env`. *Expect:* the model in the first file; the env content in the second; no content on stdout.
- `CFG-GET-06`: *Pre:* three versions. *Steps:* `config <rst> --version 1`. *Expect:* the content of version 1.
- `CFG-GET-07`: *Steps:* `config <rst> -f env --env-prefix APP_`; `--env-prefix 1X`. *Expect:* every name starts with `APP_`; the second fails with exit 2.
- `CFG-GET-08`: *Pre:* a binding to a secret. *Steps:* `config <rst> -r` as a Contributor, then as ContributorWithSecrets. *Expect:* refused (exit 44) with the secret named; then resolved.

### Set, Patch and Delete (`ConfigWriteTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| CFG-SET-01 | – | `config set -x` merges inline properties into the stored content | Elementary | Editor | | `set`, `arg0`, `-x`, `--properties` | `Config_SetProperties_Merges` |
| CFG-SET-02 | – | `config create` is an alias of `set` | Low | Editor | | `create` | `Config_CreateAlias_Sets` |
| CFG-SET-03 | – | `--replace` removes the properties that are not given | High | Editor | | `--replace` | `Config_Replace_RemovesOthers` |
| CFG-SET-04 | – | `-i` imports the content from a file | High | Editor | | `-i`, `--import` | `Config_Import_FromFile` |
| CFG-SET-05 | – | `-c` and `-l` set custom properties and labels on the configuration | Medium | Editor | | `-c`, `--custom-properties`, `-l`, `--labels` | `Config_CustomPropertiesAndLabels` |
| CFG-SET-06 | – | `set -r` prints the resolved configuration after the write | Medium | Editor | | `-r`, `--resolved` | `Config_SetResolved_PrintsResolved` |
| CFG-SET-07 | – | A content that violates the schema fails, and `-f` stores it | High | Editor | | `-f`, `--force` | `Config_SchemaViolation_Force` |
| CFG-SET-08 | – | `config patch -i -y` applies a JSON Patch | High | Editor | | `patch`, `arg0`, `-i`, `--import`, `-y`, `--yes` | `Config_Patch_Applies` |
| CFG-SET-09 | – | `patch` shows the change and asks in a terminal; No aborts with exit 40 | Medium | Editor | T | `patch` | `Config_Patch_ConfirmOrAbort` |
| CFG-SET-10 | – | `patch -f` stores a patched content that violates the schema; a failing `test` op changes nothing | Medium | Editor | | `-f`, `--force` | `Config_Patch_ForceAndTest` |
| CFG-SET-11 | – | `config delete` (and `remove`) deletes the configuration | Medium | Editor | | `delete`, `remove`, `arg0` | `Config_Delete_Removes` |
| CFG-SET-12 | – | A Reader's `config set` is refused with exit 44 | High | Reader | | `set` | `Config_SetAsReader_Unauthorized` |

- `CFG-SET-01`: *Steps:* `config set <rst> -x a=1 -x b.c=true`; `--properties d=x`. *Expect:* the content has a=1, b.c=true and d="x"; a new version.
- `CFG-SET-02`: *Steps:* `config create <rst> -x e=2`. *Expect:* as set.
- `CFG-SET-03`: *Steps:* `config set <rst> -x only=1 --replace`. *Expect:* the content is `{ "only": 1 }`.
- `CFG-SET-04`: *Steps:* `config set <rst> -i c.json`; `--import c2.yaml` (when YAML is accepted, else JSON). *Expect:* the content of the files merged.
- `CFG-SET-05`: *Steps:* `config set <rst> -x a=1 -c owner=t --custom-properties n=2 -l x --labels y`. *Expect:* the model shows them.
- `CFG-SET-06`: *Steps:* `config set <rst>.<unt> -x a=1 -r`; `--resolved`. *Expect:* the printed configuration includes the inherited values.
- `CFG-SET-07`: *Pre:* a type schema requiring `a` to be a number. *Steps:* `config set <rst> -x a=text`; `-f`; `--force`. *Expect:* exit 2 with the violation; then stored with a warning.
- `CFG-SET-08`: *Steps:* `config patch <rst> -i p.json -y` (replace, add, remove); `--import p2.json --yes`. *Expect:* the content changed as patched; a version each.
- `CFG-SET-09`: *Steps:* `config patch <rst> -i p.json` in a terminal; No; then Yes. *Expect:* exit 40 and no new version; then applied.
- `CFG-SET-10`: *Steps:* a patch that breaks the schema with and without `-f`; a patch with a failing `test` op. *Expect:* refused, then stored; the `test` failure changes nothing.
- `CFG-SET-11`: *Steps:* `config delete <rst>`; `config remove <rst2>`. *Expect:* `config <rst>` returns no configuration.
- `CFG-SET-12`: *Steps:* `config set <rst> -x a=1` in the Reader project. *Expect:* exit 44.

### Versions, Diff, Composition and Edit (`ConfigHistoryTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| CFG-HIS-01 | – | `config versions` lists the versions newest first | Medium | Reader | | `versions`, `arg0` | `Config_Versions_Listed` |
| CFG-HIS-02 | – | `config diff` compares the latest with its previous version, or two given versions | High | Reader | | `diff`, `arg0`, `arg1`, `arg2` | `Config_Diff_Versions` |
| CFG-HIS-03 | – | `diff --view-to-compare` compares with another view | Medium | Reader | | `--view-to-compare` | `Config_DiffView_ComparesViews` |
| CFG-HIS-04 | – | `config composition` shows the merged documents and the step that set each line; `-e` saves it | Medium | Reader | | `composition`, `arg0`, `-e`, `--export` | `Config_Composition_ShowsSteps` |
| CFG-HIS-05 | – | `config edit` stores the editor's change as a new version | High | Editor | E | `edit`, `arg0` | `Config_Edit_StoresChange` |
| CFG-HIS-06 | – | `edit` without a change stores nothing; a schema violation needs `-f` | Medium | Editor | E | `-f`, `--force` | `Config_Edit_NoChangeOrForce` |

- `CFG-HIS-01`: *Pre:* three versions. *Steps:* `config versions <rst>`. *Expect:* 3, 2, 1 with times and authors.
- `CFG-HIS-02`: *Steps:* `config diff <rst>`; `config diff <rst> 3 1`; `config diff <rst> 2`. *Expect:* 3 against 2; 3 against 1; 2 against 1, each showing the changed lines.
- `CFG-HIS-03`: *Pre:* the configuration differs in view B. *Steps:* `config diff <rst> -v A --view-to-compare B`. *Expect:* A as from, B as to.
- `CFG-HIS-04`: *Pre:* a type template, a parent configuration and the own content. *Steps:* `config composition <rst>.<unt>`; `-e comp.json`; `--export comp2.json`. *Expect:* the documents in merge order and the step per line; the same in the files.
- `CFG-HIS-05`: *Pre:* the fake editor sets `a` to 5. *Steps:* `config edit <rst>`. *Expect:* a new version with a=5.
- `CFG-HIS-06`: *Steps:* the editor leaves the file unchanged; then it writes a schema-breaking value, with and without `-f` / `--force`. *Expect:* "no change" (exit 42) and no version; refused, then stored.

### GitHub Actions and Masking (`ConfigGitHubTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| CFG-GHA-01 | – | `--github-env` with `--env-prefix` writes the resolved values into `GITHUB_ENV` and masks bound values | Elementary | Secrets | | `--github-env`, `--env-prefix` | `GitHubEnv_Prefixed_ExportsAndMasks` |
| CFG-GHA-02 | – | `--github-env` without `--env-prefix` fails, and `--allow-unprefixed` allows it | Medium | Secrets | | `--allow-unprefixed` | `GitHubEnv_Unprefixed_NeedsAllow` |
| CFG-GHA-03 | – | `--mask bound` and `--mask all` print `::add-mask::` for bound values or every value | Medium | Secrets | | `--mask` | `Mask_BoundOrAll` |
| CFG-GHA-04 | – | An unresolved binding fails the export, and `--allow-unresolved` exports it as it is | Medium | Secrets | | `--allow-unresolved` | `GitHubEnv_Unresolved_NeedsAllow` |
| CFG-GHA-05 | – | `--github-env` or `--mask` without `-r` fails with exit 2 | Low | Reader | | `--github-env`, `--mask` | `GitHubEnv_WithoutResolved_Fails` |

- `CFG-GHA-01`: *Pre:* `GITHUB_ENV` set to a temp file; a configuration with a secret binding. *Steps:* `config <rst> -r --github-env --env-prefix APP_`. *Expect:* the file has `APP_…` lines (multiline values in heredoc form); stdout has `::add-mask::` for the bound value only; no content printed.
- `CFG-GHA-02`: *Steps:* `config <rst> -r --github-env`; then with `--allow-unprefixed`. *Expect:* exit 2 naming both options; then names without a prefix.
- `CFG-GHA-03`: *Steps:* `config <rst> -r --mask bound`; `--mask all`. *Expect:* masks for the bound values, then for every string and number.
- `CFG-GHA-04`: *Pre:* a binding to a missing key. *Steps:* `config <rst> -r --github-env --env-prefix A_`; then `--allow-unresolved`. *Expect:* a non-zero exit naming the binding; then the expression is exported as it is.
- `CFG-GHA-05`: *Steps:* `config <rst> --github-env --env-prefix A_`; `config <rst> --mask all`. *Expect:* exit 2 naming `--resolved`.

## Templates

### Templates (`TemplateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| TPL-TPL-01 | – | `story template` (and `templates`) returns the template of a type | High | Reader | | `template`, `templates`, `arg0` | `Template_Get_Returns` |
| TPL-TPL-02 | – | `template set -x` merges properties, and `--replace` replaces the content | High | Editor | P | `set`, `arg0`, `-x`, `--properties`, `--replace` | `Template_Set_MergeOrReplace` |
| TPL-TPL-03 | – | `template create -i` imports a template from a file | Medium | Editor | P | `create`, `-i`, `--import` | `Template_Import_FromFile` |
| TPL-TPL-04 | – | `template -e` saves it, and `--version` returns an old version | Medium | Reader | P | `-e`, `--export`, `--version` | `Template_ExportAndVersion` |
| TPL-TPL-05 | – | `template versions` and `template diff` show the history | Medium | Reader | P | `versions`, `arg0`, `diff`, `arg0`, `arg1`, `arg2` | `Template_VersionsAndDiff` |
| TPL-TPL-06 | – | `template edit` stores the editor's change | Medium | Editor | E, P | `edit`, `arg0` | `Template_Edit_StoresChange` |
| TPL-TPL-07 | – | `template delete` (and `remove`) deletes it, and the history stays | Medium | Editor | P | `delete`, `remove`, `arg0` | `Template_Delete_KeepsHistory` |
| TPL-TPL-08 | – | An unknown type code fails with exit 2; a Reader's set is refused | Medium | Reader | | `arg0`, `set` | `Template_InvalidTypeOrReader_Fails` |

- `TPL-TPL-01`: *Pre:* a template for `unt`. *Steps:* `story template unt`; `story templates unt`. *Expect:* the template content.
- `TPL-TPL-02`: *Steps:* `template set unt -x a=1`; `--properties b=2`; `-x c=3 --replace`. *Expect:* {a,b} after the merges, {c} after the replace.
- `TPL-TPL-03`: *Steps:* `template create sbt -i t.json`; `--import t2.json`. *Expect:* the file's content.
- `TPL-TPL-04`: *Steps:* `template unt -e t.json`; `--export t2.json`; `template unt --version 1`. *Expect:* the files hold it; version 1's content.
- `TPL-TPL-05`: *Steps:* `template versions unt`; `template diff unt`; `template diff unt 3 1`. *Expect:* the versions; the diffs.
- `TPL-TPL-06`: *Steps:* the fake editor adds `x`; `template edit unt`. *Expect:* a new version with `x`.
- `TPL-TPL-07`: *Steps:* `template delete unt`; `template remove sbt`; `template versions unt`. *Expect:* no current template; the versions still listed.
- `TPL-TPL-08`: *Steps:* `template xyz`; `template set unt -x a=1` as a Reader. *Expect:* exit 2 listing the codes; exit 44.

## Schemas

### Type Schemas (`SchemaTypeTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| SCH-TYP-01 | – | `schema type set -i` stores a type schema, and `schema type get` returns it | High | Editor | P | `schema`, `type`, `set`, `arg0`, `-i`, `--import`, `get`, `arg0` | `SchemaType_SetGet` |
| SCH-TYP-02 | – | `schema type create` is an alias, and a schema the configurations break fails without `-f` | Medium | Editor | P | `create`, `-f`, `--force` | `SchemaType_Breaking_NeedsForce` |
| SCH-TYP-03 | – | `get --version`, `versions` and `diff` with `--format json` and `unified` | Medium | Reader | P | `--version`, `versions`, `arg0`, `diff`, `arg0`, `arg1`, `arg2`, `--format` | `SchemaType_History` |
| SCH-TYP-04 | – | `schema type edit` stores the editor's change; `-f` for a breaking change | Medium | Editor | E, P | `edit`, `arg0`, `-f`, `--force` | `SchemaType_Edit` |
| SCH-TYP-05 | – | `schema type delete` (and `remove`) deletes it | Medium | Editor | P | `delete`, `remove`, `arg0` | `SchemaType_Delete` |

- `SCH-TYP-01`: *Steps:* `story schema type set unt -i s.json`; `--import s2.json`; `schema type get unt`. *Expect:* the stored schema equals s2.json.
- `SCH-TYP-02`: *Pre:* a `unt` configuration without `req`. *Steps:* `schema type create unt -i requires-req.json`; then `-f`; `--force`. *Expect:* refused with the non-complying keys; then stored with a warning.
- `SCH-TYP-03`: *Steps:* three versions; `schema type get unt --version 1`; `schema type versions unt`; `schema type diff unt`; `diff unt 3 1 --format unified`; `--format json`. *Expect:* version 1; 3 versions; a JSON diff and a unified diff.
- `SCH-TYP-04`: *Steps:* the fake editor adds a property; `schema type edit unt`; a breaking edit with `-f` and `--force`. *Expect:* new versions.
- `SCH-TYP-05`: *Steps:* `schema type delete unt`; `schema type remove sbt`. *Expect:* `get` reports no schema; `versions` still lists the history.

### Annotation Schemas (`SchemaAnnotationTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| SCH-ANN-01 | – | `schema annotation set` and `get` for one annotation | High | Editor | P | `annotation`, `set`, `arg0`, `-i`, `--import`, `get`, `arg0` | `SchemaAnnotation_SetGet` |
| SCH-ANN-02 | – | `create` alias, `-f` for configurations that break it | Medium | Editor | P | `create`, `-f`, `--force` | `SchemaAnnotation_Breaking_NeedsForce` |
| SCH-ANN-03 | – | `get --version`, `versions` and `diff` with both formats | Medium | Reader | P | `--version`, `versions`, `arg0`, `diff`, `arg0`, `arg1`, `arg2`, `--format` | `SchemaAnnotation_History` |
| SCH-ANN-04 | – | `edit` with the fake editor, `-f` for a breaking change | Medium | Editor | E, P | `edit`, `arg0`, `-f`, `--force` | `SchemaAnnotation_Edit` |
| SCH-ANN-05 | – | `delete` and `remove` | Low | Editor | P | `delete`, `remove`, `arg0` | `SchemaAnnotation_Delete` |

- `SCH-ANN-01` … `SCH-ANN-05`: as `SCH-TYP-01` … `05` with `schema annotation <rst>` instead of `schema type <code>`. The schema applies to `<rst>` only: a configuration of another responsibility that breaks it still saves.

### Descendant Schemas (`SchemaDescendantTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| SCH-DSC-01 | – | `schema descendant set` and `get` for a type below an ancestor | High | Editor | P | `descendant`, `set`, `arg0`, `arg1`, `-i`, `--import`, `get`, `arg0`, `arg1` | `SchemaDescendant_SetGet` |
| SCH-DSC-02 | – | A type that cannot descend from the ancestor fails with InvalidKey | Medium | Editor | P | `arg1` | `SchemaDescendant_InvalidType_Fails` |
| SCH-DSC-03 | – | `create` alias, `-f`, `get --version`, `versions` and `diff` with both formats | Medium | Editor | P | `create`, `-f`, `--force`, `--version`, `versions`, `arg0`, `arg1`, `diff`, `arg0`, `arg1`, `arg2`, `arg3`, `--format` | `SchemaDescendant_History` |
| SCH-DSC-04 | – | `edit` with the fake editor, `-f` for a breaking change | Medium | Editor | E, P | `edit`, `arg0`, `arg1`, `-f`, `--force` | `SchemaDescendant_Edit` |
| SCH-DSC-05 | – | `delete` and `remove` | Low | Editor | P | `delete`, `remove`, `arg0`, `arg1` | `SchemaDescendant_Delete` |

- `SCH-DSC-01`: *Steps:* `schema descendant set <rst> unt -i s.json`; `--import`; `schema descendant get <rst> unt`. *Expect:* the schema applies to the units below `<rst>` only.
- `SCH-DSC-02`: *Steps:* `schema descendant set <rst> sbt -i s.json`. *Expect:* a non-zero exit with InvalidKey.
- `SCH-DSC-03`: *Steps:* `create`, a breaking set with `-f`/`--force`, `get <rst> unt --version 1`, `versions <rst> unt`, `diff <rst> unt`, `diff <rst> unt 3 1 --format unified`. *Expect:* as `SCH-TYP-03`.
- `SCH-DSC-04`: *Steps:* `schema descendant edit <rst> unt` with the fake editor; a breaking edit with `-f`/`--force`. *Expect:* new versions.
- `SCH-DSC-05`: *Steps:* `schema descendant delete <rst> unt`; `remove <rst> usg`. *Expect:* no current schema; the history kept.

### Combined Schema (`SchemaDefinitionTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| SCH-DEF-01 | – | `schema definition` returns the combined schema of type, ancestor and annotation schemas | Medium | Reader | P | `definition`, `arg0` | `SchemaDefinition_CombinesSchemas` |
| SCH-DEF-02 | – | A Reader reads schemas, and a Reader's `schema type set` is refused | High | Reader | | `get`, `set` | `Schema_ReaderRights` |
| SCH-DEF-03 | – | A machine needs Schema.Write with Configuration.Write to set a schema | Medium | Machine | M | `set` | `Schema_MachineScopes` |
| SCH-DEF-04 | – | A non-JSON-Schema file fails with exit 2 and stores nothing | Low | Editor | P | `-i` | `Schema_InvalidFile_Fails` |
| SCH-DEF-05 | – | A type code outside rst, unt, sbt, usg, cnt, exe, uxe fails with exit 2 | Low | Reader | | `arg0` | `Schema_InvalidTypeCode_Fails` |

- `SCH-DEF-01`: *Pre:* a type schema of `unt`, a descendant schema under `<rst>` and an annotation schema of `<rst>.<unt>`. *Steps:* `schema definition <rst>.<unt>`. *Expect:* one schema with the constraints of all three.
- `SCH-DEF-02`: *Steps:* `schema type get unt` and `schema type set unt -i s.json` as a Reader. *Expect:* the get works; the set gets exit 44.
- `SCH-DEF-03`: *Steps:* as a `-w` machine, then a `-w -m SchemaWrite` machine, `schema type set unt -i s.json`. *Expect:* exit 44 naming Schema.Write; then stored.
- `SCH-DEF-04`: *Steps:* `schema type set unt -i not-a-schema.txt`. *Expect:* exit 2, no new version.
- `SCH-DEF-05`: *Steps:* `schema type get abc`. *Expect:* exit 2 listing the codes.

## Key-Values

### Pairs (`KeyValuePairTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| KV-PAR-01 | – | `kv set` with `--protection public` and `kv get` round-trip a public pair | Elementary | Editor | | `kv`, `set`, `arg0`, `arg1`, `--protection`, `get`, `arg0` | `Kv_SetGetPublic_RoundTrip` |
| KV-PAR-02 | – | `kv create` is an alias, and an update without `--protection` keeps the level | Medium | Editor | | `create`, `--protection` | `Kv_UpdateKeepsLevel` |
| KV-PAR-03 | – | A new key without `--protection` fails with exit 2 | Medium | Editor | | `--protection` | `Kv_NewWithoutLevel_Fails` |
| KV-PAR-04 | – | `--stdin` and `-i` read the value from standard input and from a file | High | Editor | | `--stdin`, `-i`, `--import` | `Kv_ValueSources_StdinAndFile` |
| KV-PAR-05 | – | `--content-type` stores the content type | Low | Editor | | `--content-type` | `Kv_ContentType_Stored` |
| KV-PAR-06 | – | `--expected-version` writes only at that version, and 0 only when the key does not exist | High | Editor | | `--expected-version` | `Kv_ExpectedVersion_Concurrency` |
| KV-PAR-07 | – | `kv get --value-only` prints the value alone, and `-e` saves the pair as JSON | Medium | Reader | | `--value-only`, `-e`, `--export` | `Kv_GetValueOnlyAndExport` |
| KV-PAR-08 | – | `kv get --version` returns an old version, and `--deleted` finds a soft-deleted key | Medium | Reader | | `--version`, `--deleted` | `Kv_GetVersionAndDeleted` |
| KV-PAR-09 | – | `--reference` stores a Key Vault reference as a public pair that a binding resolves | Low | Editor | X:KeyVaultReference | `--reference` | `Kv_Reference_Resolves` |
| KV-PAR-10 | – | A Reader's `kv set` is refused, and a machine needs Configuration.Write | Medium | Reader | M | `set` | `Kv_WriteRights` |

- `KV-PAR-01`: *Steps:* `story kv set <key> v1 --protection public`; `kv get <key>`. *Expect:* the pair with value `v1`, level public, version 1.
- `KV-PAR-02`: *Steps:* `kv create <key> v2` on an existing standard secret. *Expect:* version 2, still standard.
- `KV-PAR-03`: *Steps:* `kv set <new key> v`. *Expect:* exit 2 naming `--protection`.
- `KV-PAR-04`: *Steps:* `kv set <key> --protection public --stdin` with `v3` on stdin; `kv set <key> -i v.txt`; `--import v2.txt`. *Expect:* the values of stdin and the files; no value in the command line.
- `KV-PAR-05`: *Steps:* `kv set <key> '{"a":1}' --protection public --content-type application/json`. *Expect:* get shows application/json.
- `KV-PAR-06`: *Steps:* `kv set <new> v --protection public --expected-version 0` twice; `kv set <new> w --expected-version 1`; again `--expected-version 1`. *Expect:* first succeeds, second fails (conflict); then succeeds, then fails.
- `KV-PAR-07`: *Steps:* `kv get <key> --value-only`; `kv get <key> -e p.json`; `--export p2.json`. *Expect:* only the value on stdout; the pair in the files.
- `KV-PAR-08`: *Steps:* `kv get <key> --version 1`; delete; `kv get <key>`; `kv get <key> --deleted`. *Expect:* v1; not found; the deleted pair with its expiry.
- `KV-PAR-09`: *Steps:* `kv set <key> kv://<vault>/<name> --reference`; a configuration binding to `<key>`; `config -r`. *Expect:* level public, and the resolved value comes from the vault.
- `KV-PAR-10`: *Steps:* `kv set <key> v --protection public` as a Reader; as a `-r` machine. *Expect:* exit 44 both.

### Secrets (`KeyValueSecretTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| KV-SEC-01 | – | `--secret` stores a secret at the project's default level, and `get` hides its value | Elementary | Secrets | | `--secret` | `Kv_Secret_HiddenByDefault` |
| KV-SEC-02 | – | `get --reveal` returns the secret's value to ContributorWithSecrets | High | Secrets | | `--reveal` | `Kv_Reveal_ReturnsValue` |
| KV-SEC-03 | – | A Contributor can neither set nor reveal a secret | High | Contributor | | `--secret`, `--reveal` | `Kv_SecretAsContributor_Refused` |
| KV-SEC-04 | – | A secret without a value source asks with hidden input in a terminal, and fails fast on redirected input | Medium | Secrets | T | `set` | `Kv_SecretPrompt_HiddenInput` |
| KV-SEC-05 | – | `--protection standard` and `hsm` store secrets at those levels | Medium | Secrets | X:Hsm | `--protection` | `Kv_ServerSideLevels` |
| KV-SEC-06 | – | A machine reads secrets only with Secrets.Read, over mTLS when the policy requires it | High | Machine | M | `--reveal` | `Kv_MachineSecrets_ScopeAndMtls` |

- `KV-SEC-01`: *Steps:* `kv set <key> s3cr3t --secret`; `kv get <key>`; `kv`. *Expect:* the default secret level; the value masked in get and list; the value never in the run's stdout.
- `KV-SEC-02`: *Steps:* `kv get <key> --reveal`; `--reveal --value-only`. *Expect:* the value; then the value alone.
- `KV-SEC-03`: *Steps:* `kv set <key> x --secret`, `kv get <secret> --reveal` as a Contributor. *Expect:* exit 44 both.
- `KV-SEC-04`: *Steps:* `kv set <key> --secret` in a terminal; type the value. Then with input redirected and no value. *Expect:* the typed value is not echoed and is stored; the redirected run fails fast with a hint to `--stdin`.
- `KV-SEC-05`: *Steps:* `kv set <k1> v --protection standard`; `kv set <k2> v --protection hsm`. *Expect:* the levels in get and list.
- `KV-SEC-06`: *Steps:* as an API key machine with `--scope SecretsRead`, `kv get <secret> --reveal`; then with the policy `--require-mtls true`; then as a certificate machine. *Expect:* revealed; refused without mTLS; revealed over mTLS.

### List, Delete and Bulk (`KeyValueBulkTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| KV-BLK-01 | – | `kv` (and `key-values`) lists the keys of the view; `--prefix` filters | High | Reader | | `kv`, `key-values`, `--prefix` | `Kv_List_Prefix` |
| KV-BLK-02 | – | `--protection` filters by levels, and `--values` shows public values with secrets masked | Medium | Reader | | `--protection`, `--values` | `Kv_List_LevelsAndValues` |
| KV-BLK-03 | – | `--deleted` lists only soft-deleted keys, `--all` both, `-k` pages | Medium | Reader | P | `--deleted`, `--all`, `-k`, `--continuation-token` | `Kv_List_DeletedAllPages` |
| KV-BLK-04 | – | `kv delete` (and `remove`) soft-deletes, with `--expected-version` | High | Editor | | `delete`, `remove`, `arg0`, `--expected-version` | `Kv_Delete_SoftDeletes` |
| KV-BLK-05 | – | `delete --permanent -y` removes every version as an Administrator; a terminal asks first | Medium | Admin | T | `--permanent`, `-y`, `--yes` | `Kv_PermanentDelete` |
| KV-BLK-06 | – | `kv restore` restores keys by list or `--prefix` | High | Editor | | `restore`, `arg0`, `--prefix` | `Kv_Restore_KeysAndPrefix` |
| KV-BLK-07 | – | `kv purge -y` removes soft-deleted keys for good (Administrator), and Contributors are refused | Medium | Admin | | `purge`, `arg0`, `--prefix`, `-y`, `--yes` | `Kv_Purge_Admin` |
| KV-BLK-08 | – | `kv protect --protection` moves keys to another level as a new version | Medium | Secrets | | `protect`, `arg0`, `--prefix`, `--protection` | `Kv_Protect_ChangesLevel` |
| KV-BLK-09 | – | `kv versions` and `kv diff` show the history of a key | Medium | Reader | | `versions`, `arg0`, `diff`, `arg0`, `arg1`, `arg2` | `Kv_VersionsAndDiff` |

- `KV-BLK-01`: *Pre:* keys `app/a`, `app/b`, `other/c`. *Steps:* `kv`; `key-values --prefix app/`. *Expect:* all three; then the two `app/` keys.
- `KV-BLK-02`: *Steps:* `kv --protection public`; `--protection public --protection standard`; `kv --values`. *Expect:* only the levels; values shown for public pairs and masked for secrets.
- `KV-BLK-03`: *Steps:* `kv --deleted`; `kv --all`; `kv -k <token>`; `--continuation-token <token>`. *Expect:* deleted with expiry; active and deleted; pages without repeats.
- `KV-BLK-04`: *Steps:* `kv delete <k1>`; `kv remove <k2> --expected-version 1` at version 2, then at 2. *Expect:* k1 soft-deleted; the first k2 delete conflicts, the second succeeds.
- `KV-BLK-05`: *Steps:* `kv delete <k> --permanent -y`; another in a terminal with `--permanent`, answer Yes; `--yes`. *Expect:* gone from `--deleted`; `get --deleted` not found.
- `KV-BLK-06`: *Steps:* delete `app/a`, `app/b`, `x`; `kv restore x`; `kv restore --prefix app/`. *Expect:* all three active at their latest versions.
- `KV-BLK-07`: *Steps:* `kv purge <k> -y`; `kv purge --prefix tmp/ --yes`; as a Contributor `kv purge <k2> -y`. *Expect:* purged; exit 44 for the Contributor.
- `KV-BLK-08`: *Steps:* `kv protect <secret> --protection standard`; `kv protect --prefix sec/ --protection standard`. *Expect:* a new version per key at the level.
- `KV-BLK-09`: *Steps:* three versions; `kv versions <k>`; `kv diff <k>`; `kv diff <k> 3 1`; `kv diff <k> 1 0`. *Expect:* 3 versions; text diffs, the last against the empty text.

### Export and Import (`KeyValueTransferTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| KV-TRN-01 | – | `kv export` writes a json document to stdout, and `-o` to a file | High | Reader | | `export`, `-o`, `--output` | `KvExport_JsonDocument` |
| KV-TRN-02 | – | `--format flat` and `env`, and the format guessed from the file name | Medium | Reader | | `--format` | `KvExport_FlatAndEnv` |
| KV-TRN-03 | – | `--prefix` and `--protection` limit the export | Medium | Reader | | `--prefix`, `--protection` | `KvExport_Filters` |
| KV-TRN-04 | – | Secrets have no value without `--include-secrets`, and with it for ContributorWithSecrets only | High | Secrets | | `--include-secrets` | `KvExport_IncludeSecrets` |
| KV-TRN-05 | – | `kv import -y` merges a json document | High | Editor | | `import`, `arg0`, `-y`, `--yes` | `KvImport_MergeJson` |
| KV-TRN-06 | – | `--format flat` and `env` imports, `--secret` globs and `--protection` for the secrets | Medium | Secrets | | `--format`, `--secret`, `--protection` | `KvImport_FlatEnvSecrets` |
| KV-TRN-07 | – | `--mode replace --prefix` deletes the other active keys under the prefix | Medium | Editor | P | `--mode`, `--prefix` | `KvImport_ReplaceUnderPrefix` |
| KV-TRN-08 | – | `--dry-run` shows the plan and writes nothing; a terminal asks before writing | Medium | Editor | T | `--dry-run` | `KvImport_DryRunAndConfirm` |

- `KV-TRN-01`: *Steps:* `kv export`; `kv export -o kv.json`; `--output out/kv.json`. *Expect:* the key/value document on stdout and in the files.
- `KV-TRN-02`: *Steps:* `kv export --format flat`; `--format env`; `-o kv.env` without `--format`. *Expect:* a flat object; .env lines; .env lines guessed from the name.
- `KV-TRN-03`: *Steps:* `kv export --prefix app/`; `--protection public --protection standard`. *Expect:* only those keys.
- `KV-TRN-04`: *Steps:* `kv export`; `kv export --include-secrets`; the same as a Contributor. *Expect:* secrets listed without values; with values; exit 44 for the Contributor.
- `KV-TRN-05`: *Steps:* `kv import kv.json -y`; `--yes`. *Expect:* the listed keys written; other keys untouched.
- `KV-TRN-06`: *Steps:* `kv import a.flat.json --format flat --secret '*_TOKEN' --protection standard -y`; `kv import .env --format env --secret 'DB_*' -y`. *Expect:* matching keys are standard secrets, the others public.
- `KV-TRN-07`: *Steps:* `kv import kv.json --mode replace --prefix app/ -y`; `--mode merge`. *Expect:* keys under `app/` not in the file are deleted, others kept; merge deletes nothing.
- `KV-TRN-08`: *Steps:* `kv import kv.json --dry-run`; `kv import kv.json` in a terminal, No, then Yes. *Expect:* the plan and no write; exit 40 and no write; then written.

### Policy and Encryption (`KeyValuePolicyTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| KV-POL-01 | – | `kv policy` shows the allowed levels, the mTLS rule, the keys and the recipients | Medium | Reader | | `policy` | `KvPolicy_Show` |
| KV-POL-02 | – | `policy set --allow --default --require-mtls --hsm-rotation-days` changes the policy | High | Admin | P | `set`, `--allow`, `--default`, `--require-mtls`, `--hsm-rotation-days` | `KvPolicy_Set_Changes` |
| KV-POL-03 | – | `policy set --expected-version` changes only at that version; a Contributor and a machine are refused | Medium | Admin | P, M | `--expected-version`, `set` | `KvPolicy_ExpectedVersionAndRights` |
| KV-POL-04 | – | `policy byok-setup` prints the application to consent to and the role to grant | Low | Admin | P | `byok-setup` | `KvPolicy_ByokSetup_PrintsSteps` |
| KV-POL-05 | – | `--byok-key --byok-tenant` set the customer key, `byok-verify` checks it, an empty `--byok-key` removes it | Low | Admin | P, X:Byok | `--byok-key`, `--byok-tenant`, `byok-verify` | `KvPolicy_Byok_SetVerifyRemove` |
| KV-POL-06 | – | `policy rotate --protection standard` (and `hsm`) rotates the key and the values stay readable | Medium | Admin | P | `rotate`, `--protection` | `KvPolicy_Rotate_KeepsValues` |
| KV-POL-07 | – | `kv set --protection byok` and `kv protect --protection byok` with the customer key | Low | Secrets | P, X:Byok | `--protection` | `Kv_Byok_StoresAndMoves` |

- `KV-POL-01`: *Steps:* `kv policy`. *Expect:* the levels, the mTLS rule, the key information, the version.
- `KV-POL-02`: *Steps:* `kv policy set --allow standard --allow hyok --default standard --require-mtls true --hsm-rotation-days 30`. *Expect:* `kv policy` shows each change; members not given keep their values.
- `KV-POL-03`: *Steps:* `policy set --require-mtls false --expected-version <old>`; with the current version; as a Contributor; as a machine. *Expect:* conflict; changed; exit 44; exit 44.
- `KV-POL-04`: *Steps:* `kv policy byok-setup`. *Expect:* the application id and the role to grant.
- `KV-POL-05`: *Steps:* `policy set --byok-key https://<vault>/keys/<name> --byok-tenant <tenant>`; `policy byok-verify`; `policy set --byok-key ""`. *Expect:* the key set; verification passes; the settings removed.
- `KV-POL-06`: *Steps:* `kv policy rotate --protection standard`; `--protection hsm` (with `X:Hsm`, otherwise only standard); `kv get <secret> --reveal`. *Expect:* a new key version and the re-wrap started; the value still readable.
- `KV-POL-07`: *Steps:* `kv set <k> v --protection byok`; `kv protect <k2> --protection byok`. *Expect:* level byok, values readable.

### Client Encryption (`KeyValueRecipientTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| KV-HYK-01 | – | `kv recipients add` registers an RSA key from a PEM file, inline, or as JWK, with `--id` | High | Admin | P | `recipients`, `add`, `--name`, `--public-key`, `--id` | `Recipients_Add_PemJwkInline` |
| KV-HYK-02 | – | A key below 3072 bits is refused | Low | Admin | P | `--public-key` | `Recipients_SmallKey_Refused` |
| KV-HYK-03 | – | `kv set --protection hyok` encrypts locally, and `get --private-key --kid` decrypts | High | Secrets | P | `--protection`, `--private-key`, `--kid` | `Hyok_SetAndDecrypt` |
| KV-HYK-04 | – | `recipients disable` stops new values for the recipient, and `enable` resumes them | Medium | Admin | P | `disable`, `enable`, `arg0` | `Recipients_DisableEnable` |
| KV-HYK-05 | – | `recipients remove -y` (and `delete`) removes a recipient, and a terminal asks first | Medium | Admin | P, T | `remove`, `delete`, `arg0`, `-y`, `--yes` | `Recipients_Remove` |
| KV-HYK-06 | – | `kv reencrypt --private-key --kid` encrypts values again for the current recipients | Medium | Secrets | P | `reencrypt`, `arg0`, `--prefix`, `--private-key`, `--kid` | `Hyok_Reencrypt_ForNewRecipients` |

- `KV-HYK-01`: *Pre:* the test generates RSA-3072 keys. *Steps:* `kv recipients add --name a --public-key a.pem`; `add --name b --public-key b.jwk --id kid-b`; `add --name c --public-key "<inline PEM>"`; `kv recipients`. *Expect:* three recipients, `kid-b` with its given id.
- `KV-HYK-02`: *Steps:* `recipients add --name s --public-key rsa2048.pem`. *Expect:* exit 2 naming the size.
- `KV-HYK-03`: *Steps:* `kv set <k> v --protection hyok`; `kv get <k> --private-key a.pem --kid <a>`; `get <k>` without the key. *Expect:* stored encrypted; decrypted to `v`; without the key the value is not shown.
- `KV-HYK-04`: *Steps:* `recipients disable <b>`; set a hyok value; decrypt with b; `recipients enable <b>`; set again. *Expect:* b cannot decrypt the value set while disabled; it can after enable.
- `KV-HYK-05`: *Steps:* `recipients remove <c> -y`; `recipients delete <b> --yes`; one more in a terminal, Yes. *Expect:* removed; old values still decrypt with the removed key.
- `KV-HYK-06`: *Steps:* add recipient d; `kv reencrypt <k> --private-key a.pem --kid <a>`; `reencrypt --prefix hyok/ --private-key a.pem --kid <a>`; decrypt with d. *Expect:* d decrypts the re-encrypted values.

### Audit and Usage (`KeyValueAuditTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| KV-AUD-01 | – | `kv audit` lists the secrets' audit trail newest first, and `--key`, `--operation`, `--from`, `--to` filter it | Medium | Admin | | `audit`, `--key`, `--operation`, `--from`, `--to` | `KvAudit_Filters` |
| KV-AUD-02 | – | `kv audit -v` filters by view, and `-k` pages | Low | Admin | P | `-v`, `-k`, `--continuation-token` | `KvAudit_ViewAndPages` |
| KV-AUD-03 | – | `kv usage` shows the last 30 days, and `--from --to` a range | Low | Admin | | `usage`, `--from`, `--to` | `KvUsage_Range` |
| KV-AUD-04 | – | A Contributor's `kv audit` and `kv usage` are refused | Low | Contributor | | `audit`, `usage` | `KvAudit_Contributor_Refused` |

- `KV-AUD-01`: *Pre:* a reveal and an update of `<secret>` in this run. *Steps:* `kv audit`; `--key <secret>`; `--operation reveal`; `--from <run start> --to <now>`. *Expect:* the events with user and time; each filter narrows to the matching events.
- `KV-AUD-02`: *Pre:* events in two views and more than a page. *Steps:* `kv audit -v B`; `-k <token>`; `--continuation-token <token>`. *Expect:* view B only; pages continue.
- `KV-AUD-03`: *Steps:* `kv usage`; `--from <day-7> --to <today>`. *Expect:* daily counts (and charges when priced) for 30 days, then for 8 days.
- `KV-AUD-04`: *Steps:* `kv audit`, `kv usage` as a Contributor. *Expect:* exit 44 both.

## Context and Errors

### Context Options (`ContextTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| GEN-CTX-01 | – | `-p` and `-v` win over the default project and view | High | Any | | `-p`, `--projectKey`, `-v`, `--view` | `Context_ExplicitWinsOverDefault` |
| GEN-CTX-02 | – | Without a default project and without `-p` a context command asks for `sform account set` and exits 43 | High | Any | | `-p` | `Context_NoProject_InteractionNeeded` |
| GEN-CTX-03 | – | A malformed `-p` fails with exit 2, an unknown view with a not-found error | Medium | Any | | `-p`, `-v` | `Context_InvalidProjectOrView_Fails` |
| GEN-CTX-04 | – | `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` target a machine, and `-p`/`-v` win over them | Medium | Machine | M | `-p`, `-v` | `Context_MachineVariables_Target` |

- `GEN-CTX-01`: *Pre:* the default set to project A, view `default`. *Steps:* `story -p <B>`; `story --projectKey <B> --view <v2>`; `story -v <v2>`. *Expect:* B's annotations; B's view v2; A's view v2.
- `GEN-CTX-02`: *Steps:* `story` with no default. *Expect:* exit 43 and the hint `sform account set`.
- `GEN-CTX-03`: *Steps:* `story -p only-org`; `story -p <A> -v <generated>`. *Expect:* exit 2 naming the format `<organizationName>.<projectName>`; a not-found error naming the view.
- `GEN-CTX-04`: *Steps:* as a machine with `STORYTELLER_PROJECT` and `STORYTELLER_VIEW`, `story`; then `-v default`. *Expect:* the variables' view; then `default`.

### Command Line and Exit Codes (`CommandLineTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Covers | Test |
|---|---|---|---|---|---|---|---|
| GEN-CLI-01 | – | `sform` without a command, and `--help` on every command, print help and exit 0 | Medium | Any | F | `sform` | `Help_EveryCommand_Prints` |
| GEN-CLI-02 | – | An unknown command or option fails with the parse code and a suggestion | Medium | Any | F | `sform` | `Parse_Unknown_Fails` |
| GEN-CLI-03 | – | No command waits for input when stdin is redirected | High | Any | | `sform` | `RedirectedInput_NeverHangs` |
| GEN-CLI-04 | – | An unreachable API fails with a service failure, not a crash | Medium | Any | F | `sform` | `Api_Unreachable_ServiceFailure` |
| GEN-CLI-05 | – | No secret, API key or token appears in the output, the log file or the artifacts of a run | High | Secrets | M | `sform` | `Output_NeverLeaksSecrets` |
| GEN-CLI-06 | – | `SFORM_HOME` keeps every file of the CLI; `SFORM_TOKEN_STORE=file` without `SFORM_HOME` is ignored | Medium | Any | F | `sform` | `StateDirectory_IsolatesFiles` |

- `GEN-CLI-01`: *Steps:* `sform`; then `<path> --help` for every command path of the command tree (read by the coverage test). *Expect:* the usage of each, listing its options and subcommands; exit 0.
- `GEN-CLI-02`: *Steps:* `sform storytelr`; `sform story --nope`. *Expect:* exit -1 (input parsing), with "Did you mean storyteller?" for the first.
- `GEN-CLI-03`: *Steps:* every command with a prompt (`account set`, `assist key`, `story delete --permanent`, `config patch`, `kv delete --permanent`, `kv import`, `kv purge`, `kv recipients remove`, `kv set --secret`, `points create`, `account register`) without its `--yes` or value, with input redirected. *Expect:* each ends within 10 seconds with a non-zero code and names the option to use.
- `GEN-CLI-04`: *Steps:* `story -p …` with `general:baseUrl` pointing to a closed port. *Expect:* exit 3 (or the documented service code), a readable message, no stack trace.
- `GEN-CLI-05`: *Steps:* run `kv get --reveal`, machine sign-in with an API key, `config -r --github-env` and `account` with `--verbose` where available. *Expect:* only the revealed value where asked; the API key, tokens and the session never appear in stdout, stderr, the log file in `SFORM_HOME/logs` or the redacted artifacts.
- `GEN-CLI-06`: *Steps:* sign in and set a default in `SFORM_HOME`; list the CLI's build folder and the user's `.42for.net`. Then set `SFORM_TOKEN_STORE=file` without `SFORM_HOME`. *Expect:* every file is under `SFORM_HOME`, nothing next to the CLI or in the user folder. The second run uses the protected store and warns that the variable was ignored.
