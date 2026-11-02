# Phase 1 of Initial Test Cases of the Admin UI: the Catalogue in Testiny

## Overview

Phase 1 of [2026-11-02 Initial Test Cases of the Admin UI and Their Automation](../2026-11-02%20Initial%20Test%20Cases%20of%20the%20Admin%20UI%20and%20Their%20Automation.md) (Linear [424-152](https://linear.app/424fun/issue/424-152/create-initial-tests-cases-and-automate-them-for-admin-ui)): the catalogue of 233 functional cases (§1 and the section "Test Case Catalogue") created in Testiny project `2SP` (id 1) through the Testiny MCP server (§2). The ten section folders, 61 sub-folders and 227 new cases were created; cases 1–6 were updated and moved into their sub-folders. No repository code changed in this phase.

## What Was Done

### 1. The catalogue

The catalogue was built in three steps. First, one reader per area of the admin UI (shell and navigation, annotations, configurations, modeling, graph, key values, access, settings and monitoring) and one for the automation framework read the code, the mock handlers, the TypeScript e2e specs and the specs and reviews of `docs/Platform/ui.admin/specs`, and drafted 291 candidate cases. Next, one curator per section applied the plan's rules (roles, modes, data rules, ownership, names) and kept 227. Last, a critic checked overlaps, gaps and rule violations across the sections. Its findings were applied: 7 duplicates removed or narrowed, 6 gaps added as cases or steps, 10 rule violations fixed (for example ACC-MCH-07 and the trusted-issuer cases moved into disposable projects, and the Reader annotation detail split into the mock-only ANN-ROL-03). The plan was committed with 233 cases.

| Priority | Cases |
|---|---|
| Elementary (Critical) | 10 |
| High | 81 |
| Medium | 121 |
| Low | 21 |

Modes: 170 B, 35 B,P, 11 B,D, 7 B,D,P, 3 B,Ph, 7 M. Roles: 100 Editor, 48 Owner, 29 Any, 25 Admin, 13 Reader, 10 Contributor, 8 Secrets.

### 2. Testiny provisioning

- **Preflight** (2026-11-01, unchanged): project id 1 is `2SP`; the fields `priority` (0 Elementary (Critical), 1 High, 2 Medium, 3 Low), `status` `READY`, `testcase_type` `FUNCTIONAL` and `automation` `AUTOMATED` exist; the folder `Elementary` held cases 1–6.
- **Folders**: one agent per section created its top folder and its sub-folders, with the section and sub-folder paragraphs of the plan as descriptions. Each top folder description also holds the role and mode legend. The folders were matched by numeric id, never by title.
- **Cases**: every new case uses the STEPS template. Its precondition starts with the reference, the role and the mode ("**Reference** `ANN-BLK-01` · **Role** Editor · **Mode** B"), then the preconditions of the plan, then "Automated: `<Class>.<Method>`". Before the upload, every bare `<placeholder>` outside a code span was wrapped in backticks, because Testiny's markdown drops raw HTML.
- **Cases 1–6**: the title, precondition and steps now follow the catalogue. The type changed from Sanity to Functional, and the priority stays Elementary (Critical). Each case was moved into its sub-folder.
- **`Elementary` folder (id 1)**: it is empty now. The MCP server cannot delete folders, so its description says that it is superseded and can be deleted.
- **Checks**: every sub-folder's case count matches the catalogue, and all 233 ids are distinct. Cases 2 and 208 were read back with their fields.
- **Content warnings** from Testiny's markdown (no failed call):
  - A URL directly before inline code was autolinked over the backtick: ACC-FED-02 (case 206) was rewritten in the xml format, and ANN-FRM-02 (case 198) step 4 reads slightly differently.
  - The `javascript:` target of ANN-FRM-01 (case 181) step 2 is stored as an empty link.
  - Escaped quotes (`\"`) in CFG-BND-01/02 (cases 214, 221) are stored as plain quotes.
  - Testiny shows inline code with double backticks.
  These change how a case reads in Testiny, not what it checks; the plan holds the exact text.

Folders (top folder id, then the sub-folder ids):

| Section | Top folder | Sub-folders |
|---|---|---|
| Access | 11 | Sign In and Account 23, Members 25, Invitations 28, My Invitations 31, Machines 34, Machine Certificates 37, Federation 40 |
| Organizations and Projects | 3 | Organizations 12, Projects 14, Views 16 |
| Navigation | 2 | Navigation Menu 19, Context Switcher 20, Command Palette 21, Keyboard Shortcuts 24, Routing 26, Detail Panel 29 |
| Annotations | 10 | Bulk Create 51, New Annotation 53, Filtering 55, Catalog 57, Detail 60, Annotation Form 62, Delete and Restore 63, Role Access 65 |
| Configurations | 4 | Create and Delete 17, Edit and Versions 22, Save Conflicts 49, Schema and Form 64, Inheritance 67, Bindings and Resolved 69, List and Navigation 71, Roles 72 |
| Modeling | 9 | Tabs and Access 32, Schemas 35, Templates 38, Export and Import 41, Deleted 43, Compare Views 46 |
| Graph | 5 | Overview 48, Legend 50, Focus and Filter 52, Toolbar 54, Node Detail 56, Create 59, Remembered State 61 |
| Key-Values | 6 | Create 27, Detail 30, Secrets 33, Versions 36, List 39, Lifecycle 42, Import and Export 45, Roles 47 |
| Settings | 8 | Project and Roles 18, Browser Settings 44, Preferences 58, Machine Authentication 66, Secrets Policy 68, Keys and Recipients 70 |
| Monitoring | 7 | Monitor Page 13, Audit Trail 15 |

### 3. Mapping of the references to Testiny ids

#### Access

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| ACC-SGN-01 | 1 (existing) | Elementary | Any | B | `SignInTests.SignIn_ShowsOrganizations` | A signed-in user sees the organizations and the target project |
| ACC-SGN-02 | 12 | High | Any | B | `SignInTests.SignIn_SignOutAndContinue_ReturnsToLastProject` | Signing out shows the sign-in page, and Continue with AuthKit signs in again |
| ACC-SGN-03 | 13 | High | Any | B | `SignInTests.SignIn_SignedOutDeepLink_ReturnsToThePage` | A signed-out deep link signs in and returns to the requested page |
| ACC-SGN-04 | 19 | Medium | Any | B | `SignInTests.SignIn_LoginPage_HandlesReturnPathAndErrors` | The login, callback and unsupported pages handle the return path, sign-in errors and the configured state |
| ACC-SGN-05 | 20 | Medium | Any | B | `SignInTests.Account_Memberships_OpenTheProject` | The Account page shows the profile and the memberships, and a membership opens its project |
| ACC-SGN-06 | 21 | Medium | Any | M | `SignInTests.Onboarding_NewUser_CreatesFirstOrganization` | A new user creates the first organization and project on onboarding |
| ACC-MEM-01 | 50 | High | Admin | B | `MemberTests.Members_ProjectPage_LocksOwnRoleAndHighlights` | The project Members page lists the members, locks the own role and highlights a member |
| ACC-MEM-02 | 52 | Medium | Owner | B,P | `MemberTests.Members_OnlyOwner_CannotBeDemotedOrRemoved` | The only Owner of a new project can be neither demoted nor removed |
| ACC-MEM-03 | 54 | Medium | Owner | M | `MemberTests.Members_ChangeRoleAndRemove_UpdatesTable` | An Owner changes the role of a member and removes another member |
| ACC-MEM-04 | 55 | Medium | Contributor | B | `MemberTests.Members_Contributor_ShowsNoAccess` | Below Administrator the Members page offers no Invite and no access |
| ACC-INV-01 | 77 | High | Admin | B | `InvitationTests.Invitations_InviteResendRevoke_EndsRevoked` | Invite one person, resend and copy the link, then revoke the invitation |
| ACC-INV-02 | 79 | Medium | Owner | B | `InvitationTests.Invitations_TypedList_SendsOneRoleAndExpiry` | Several typed addresses get one invitation each, with one role and expiry |
| ACC-INV-03 | 80 | Medium | Owner | B | `InvitationTests.Invitations_PastedListToOrganization_SendsEach` | A list pasted from a mail client invites people to the organization |
| ACC-INV-04 | 82 | High | Admin | B | `InvitationTests.Invitations_InvalidAndPendingAddresses_AreRefused` | Missing, invalid and already invited addresses are refused |
| ACC-MYI-01 | 104 | Medium | Admin | B | `MyInvitationTests.MyInvitations_SelfInvitation_DeclineThenAccept` | A self-invitation is declined on Invitations, and a second one is accepted |
| ACC-MYI-02 | 106 | Medium | Any | M | `MyInvitationTests.MyInvitations_InvitedNewUser_JoinsProject` | An invited user without an account joins through Invitations |
| ACC-MYI-03 | 107 | Low | Any | B | `MyInvitationTests.MyInvitations_LandingSignedOut_ContinuesToSignIn` | The invitation landing asks a signed-out visitor to continue to the sign-in |
| ACC-MCH-01 | 132 | High | Editor | B | `MachineTests.Machines_CreateApiKey_ShowsCredentialsOnce` | Create an API key machine, store its one-time credentials and delete it |
| ACC-MCH-02 | 135 | High | Editor | B | `MachineTests.Machines_ResetApiKey_ShowsNewKey` | Reset replaces the API key, and Delete needs the exact id |
| ACC-MCH-03 | 139 | Medium | Admin | B | `MachineTests.Machines_Presets_ReplaceScopes` | Presets and scope rules of New machine |
| ACC-MCH-04 | 141 | Medium | Admin | B | `MachineTests.Machines_ManagementAndSecretsScopes_ShowOnDetail` | A machine with management and secrets scopes, and its detail panel |
| ACC-MCH-05 | 160 | High | Contributor | B | `MachineTests.Machines_Contributor_GivesRuntimeScopesOnly` | A Contributor gives neither management nor secrets scopes |
| ACC-MCH-06 | 161 | Medium | Contributor | M | `MachineTests.Machines_Contributor_CannotResetElevatedMachines` | A Contributor can neither reset nor delete machines with elevated scopes |
| ACC-MCH-07 | 162 | Medium | Owner | B,P | `MachineTests.Machines_AnnotationRestriction_ShowsKey` | A machine restricted to an annotation |
| ACC-MCH-08 | 163 | Low | Reader | B | `MachineTests.Machines_Reader_ShowsNoAccess` | A Reader has no access to Machines |
| ACC-CRT-01 | 182 | Medium | Owner | B,P | `MachineCertificateTests.Certificates_CertificateMachine_DownloadsPfx` | A Certificate machine shows its certificate once and downloads it |
| ACC-CRT-02 | 183 | Medium | Owner | B,P | `MachineCertificateTests.Certificates_SharedCertificateMachine_ResetsApiKeyOnly` | A CertificateAndApiKey machine uses a shared certificate, and its reset issues only a new API key |
| ACC-CRT-03 | 184 | High | Owner | B,P | `MachineCertificateTests.Certificates_IssueSharedCertificate_DownloadsAndRevokes` | Issue a shared certificate, download it and revoke it with its thumbprint prefix |
| ACC-FED-01 | 204 | High | Owner | B,P | `FederationTests.TrustedIssuers_AddEditDelete_UpdatesList` | Add, edit and delete a trusted issuer from the GitHub template |
| ACC-FED-02 | 206 | Medium | Owner | B,P | `FederationTests.TrustedIssuers_TemplatesAndValidation_GuideInput` | Issuer templates fill the dialog, and invalid or taken names and an http issuer are refused |
| ACC-FED-03 | 209 | High | Owner | B,P | `FederationTests.Federation_NoIssuerToFederatedMachine_ShowsSetup` | From no trusted issuer to a Federated machine with its setup |
| ACC-FED-04 | 216 | Medium | Owner | B,P | `FederationTests.Federation_EditRules_SavesAndValidates` | Federation rules are set up and edited on the machine detail |
| ACC-FED-05 | 217 | Low | Owner | B,P | `FederationTests.TrustedIssuers_KeySources_RequireJwks` | The JWKS URI and Pasted JWKS key sources need their keys, and the Key source column shows them |

#### Organizations and Projects

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| ORG-ORG-01 | 8 | High | Any | B | `OrganizationTests.Organization_Create_ListsItWithFirstProject` | New organization creates the organization with its first project and lists it |
| ORG-ORG-02 | 23 | Medium | Owner | B | `OrganizationTests.Organization_CreateInvalid_ShowsFieldErrors` | New organization validates the names and rejects an existing project |
| ORG-PRJ-01 | 5 (existing) | Elementary | Owner | B | `ProjectTests.Project_Create_OpensSettings` | New project opens its annotations and its Settings show the description |
| ORG-PRJ-02 | 26 | Medium | Owner | B | `ProjectTests.Project_CreateInvalid_ShowsFieldErrors` | New project validates the name and rejects an existing project |
| ORG-PRJ-03 | 28 | Medium | Reader | M | `ProjectTests.Project_CreateWithoutOrganizationRole_IsDisabled` | A project member without an organization role cannot create projects |
| ORG-VEW-01 | 31 | High | Owner | B,P | `ProjectViewTests.Views_Create_LinksCompareAndOpensView` | New view creates a view that Compare links to and the list opens |
| ORG-VEW-02 | 34 | Medium | Owner | B,P | `ProjectViewTests.Views_RegisterDefault_EditsDescription` | Register the discovered default view and edit its description |
| ORG-VEW-03 | 36 | Medium | Admin | B | `ProjectViewTests.Views_CreateInvalid_ShowsNameErrors` | New view validates the name and rejects an existing view |
| ORG-VEW-04 | 37 | Medium | Reader | B | `ProjectViewTests.Views_Reader_ListsWithoutActions` | A Reader lists the views without Compare or managing actions |
| ORG-VEW-05 | 38 | Low | Contributor | B | `ProjectViewTests.Views_Contributor_ComparesWithoutManaging` | A Contributor compares views but cannot create, register or edit them |

#### Navigation

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| NAV-MNU-01 | 6 (existing) | Elementary | Any | B | `NavigationMenuTests.Navigation_MobileMenu_OpensPages` | Navigation menu opens the pages of the view, the project and the app |
| NAV-MNU-02 | 22 | High | Any | B | `NavigationMenuTests.Navigation_OtherPages_KeepLastViewSections` | Pages outside the view keep the sections of the last view |
| NAV-MNU-03 | 24 | High | Admin | B | `NavigationMenuTests.Navigation_AdministratorMenu_ListsEveryEntryUnlocked` | An Administrator's menu lists every entry without locks |
| NAV-MNU-04 | 27 | Medium | Contributor | B | `NavigationMenuTests.Navigation_ContributorMenu_HidesAdministration` | A Contributor's menu leaves out Members, Monitor and Export & Import |
| NAV-MNU-05 | 30 | Medium | Reader | B | `NavigationMenuTests.Navigation_ReaderMenu_LocksEditingSections` | A Reader's menu locks the editing sections |
| NAV-MNU-06 | 33 | Medium | Any | B,D | `NavigationMenuTests.Sidebar_Unpin_MovesMenuBehindButtonAndIsKept` | Unpinning the sidebar moves the menu behind Open navigation and is kept |
| NAV-MNU-07 | 35 | Low | Any | B,D | `NavigationMenuTests.Sidebar_IconRail_ExpandsCollapsesAndToggles` | The icon rail expands and collapses, and [ toggles the docked sidebar |
| NAV-CTX-01 | 66 | High | Any | B,D | `NavigationContextSwitcherTests.ContextSwitcher_PickView_KeepsSection` | The view switcher changes the view, keeps the section and marks the default view |
| NAV-CTX-02 | 68 | High | Owner | B,P | `NavigationContextSwitcherTests.ContextSwitcher_PickProject_KeepsSection` | The project switcher keeps the project section and the view section |
| NAV-CTX-03 | 70 | Medium | Any | B | `NavigationContextSwitcherTests.ContextSwitcher_PickOrganization_OpensOverview` | The organization switcher lists the organizations and opens an overview |
| NAV-PAL-01 | 72 | High | Any | B | `NavigationCommandPaletteTests.CommandPalette_PagesAndProjects_Navigate` | The command palette opens pages and projects, also from a text box |
| NAV-PAL-02 | 75 | Medium | Editor | B | `NavigationCommandPaletteTests.CommandPalette_EditorActions_FindAndRecallAnnotation` | The palette's editor actions create an annotation that the palette then finds and lists as recent |
| NAV-PAL-03 | 76 | Medium | Any | B | `NavigationCommandPaletteTests.Theme_SwitchInPaletteAndAccount_IsKept` | The theme switches in the palette and on the Account page and is kept |
| NAV-KEY-01 | 115 | Medium | Any | B | `NavigationShortcutTests.Shortcuts_Help_OpensFromKeyUserMenuAndPalette` | The keyboard shortcuts help opens from ?, the user menu and the palette |
| NAV-KEY-02 | 116 | Medium | Any | B | `NavigationShortcutTests.Shortcuts_GoTo_OpenSectionsOfLastView` | Go-to shortcuts open the sections of the last view and are ignored in text boxes |
| NAV-RTE-01 | 117 | Medium | Any | B | `NavigationRoutingTests.Routing_UnknownAddress_ShowsPageNotFound` | An unknown address shows Page not found, and Go home opens the last view |
| NAV-RTE-02 | 119 | High | Any | B | `NavigationRoutingTests.Routing_ForeignContext_ShowsNoAccess` | An organization or a project outside the memberships shows No access |
| NAV-RTE-03 | 120 | Medium | Any | B | `NavigationRoutingTests.Routing_Home_OpensLastContext` | Home, the project address and the app root open the last context |
| NAV-RTE-04 | 121 | Medium | Any | B | `NavigationRoutingTests.PageMemory_ReturnToSection_RestoresItsFilter` | Each section of a view comes back with its own filter until a reload |
| NAV-RTE-05 | 122 | Low | Admin | B | `NavigationRoutingTests.Routing_OldProjectLinks_OpenTheirSections` | Old project links open their new sections |
| NAV-DET-01 | 142 | Medium | Editor | B,D | `NavigationDetailPanelTests.DetailPanel_PinAndResize_KeptForEveryDetail` | The detail pins, resizes by keyboard and keeps its mode for every detail |
| NAV-DET-02 | 143 | Medium | Editor | B,D | `NavigationDetailPanelTests.DetailPanel_FullScreen_SurvivesReload` | The detail goes full screen and stays so after a reload |
| NAV-DET-03 | 144 | Medium | Editor | B,Ph | `NavigationDetailPanelTests.DetailPanel_Phone_CoversListUntilBack` | On a phone the detail covers the list until Back to the list |

#### Annotations

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| ANN-BLK-01 | 2 (existing) | Elementary | Editor | B | `AnnotationBulkCreateTests.Annotations_CreateResponsibility_AppearsInGrid` | Bulk create a responsibility and find it in the grid |
| ANN-BLK-02 | 18 | High | Editor | B | `AnnotationBulkCreateTests.BulkCreate_CompactLines_CreatesMissingAncestors` | Compact lines create their missing ancestors |
| ANN-BLK-03 | 64 | High | Editor | B | `AnnotationBulkCreateTests.BulkCreate_ReviewProblems_BlockUntilRemoved` | Review marks existing, duplicate and invalid lines and enforces the limit |
| ANN-BLK-04 | 67 | Medium | Editor | B | `AnnotationBulkCreateTests.BulkCreate_DeletedKey_OffersRestore` | Bulk create over a deleted key offers Restore |
| ANN-NEW-01 | 69 | High | Editor | B | `AnnotationCreateTests.NewAnnotation_Execution_CreatesMissingAncestors` | New annotation creates an execution with its missing ancestors |
| ANN-NEW-02 | 71 | Medium | Editor | B | `AnnotationCreateTests.NewAnnotation_InvalidOrExistingKey_DisablesCreate` | New annotation rejects dotted names and existing keys |
| ANN-NEW-04 | 73 | Medium | Editor | B | `AnnotationCreateTests.NewAnnotation_DeletedKey_RestoresOrRecreates` | New annotation over a deleted key: Cancel, Restore and update, Create new |
| ANN-FLT-01 | 110 | High | Editor | B | `AnnotationFilterTests.AnnotationFilter_Qualifiers_NarrowTheCatalog` | Filter qualifiers, negation, OR, chips and count |
| ANN-FLT-02 | 111 | Low | Any | B | `AnnotationFilterTests.AnnotationFilter_InvalidTerms_WarnAndAreIgnored` | Invalid filter terms warn and are ignored; the syntax examples fill the box |
| ANN-FLT-03 | 112 | Medium | Any | B,Ph | `AnnotationFilterTests.AnnotationFilter_PhoneSheet_BuildsTheQuery` | Filters bottom sheet on a phone |
| ANN-CAT-01 | 113 | High | Editor | B,D | `AnnotationCatalogTests.AnnotationTree_Perspectives_ShowEveryPath` | Tree shows every path by subject and by responsibility |
| ANN-CAT-02 | 114 | High | Editor | B,D | `AnnotationCatalogTests.AnnotationBulkBar_SelectedRows_ChangeDeleteAndUndo` | Bulk bar disables, enables, adds and removes a label, and deletes selected rows with Undo |
| ANN-CAT-03 | 145 | Medium | Editor | B | `AnnotationCatalogTests.AnnotationCatalog_EmptyView_OffersCreateActions` | An empty view shows the empty state with create actions |
| ANN-CAT-04 | 146 | Low | Editor | B | `AnnotationCatalogTests.AnnotationCatalog_Reload_ShowsAnnotationsCreatedElsewhere` | Reload the catalog shows an annotation created elsewhere |
| ANN-CAT-05 | 148 | Medium | Editor | B | `AnnotationCatalogTests.AnnotationCatalog_ReturnFromSection_RestoresPageMemory` | Returning from another section restores the filter, detail, tab, selection and sorting |
| ANN-DET-01 | 152 | High | Editor | B | `AnnotationDetailTests.AnnotationDetail_OpenFromList_ClosesBackToTheFilter` | Open an annotation from the list and close it back to the filtered list |
| ANN-DET-02 | 154 | Medium | Editor | B,D | `AnnotationDetailTests.AnnotationDetail_Keyboard_MovesOpensAndCloses` | Keyboard: J and K move, Enter opens, Escape closes, / focuses the filter |
| ANN-DET-03 | 176 | Medium | Editor | B | `AnnotationDetailTests.AnnotationDetail_MoreActions_CopyAndShowInGraph` | More actions copy the link and the key, and the header links lead to the graph and the configuration |
| ANN-DET-04 | 177 | Medium | Editor | B | `AnnotationDetailTests.AnnotationDetail_Lineage_LinksAncestorsAndDescendants` | Breadcrumb, Lineage and Relations lead through ancestors and descendants |
| ANN-DET-05 | 178 | Medium | Editor | B | `AnnotationDetailTests.AnnotationHistory_CreateAndUpdate_ListsEntries` | History lists the creation, the update and the ancestor cause |
| ANN-DET-06 | 180 | Low | Any | B | `AnnotationDetailTests.AnnotationDetail_OldTabUrls_Redirect` | Old annotation tab URLs open the configuration tabs |
| ANN-FRM-01 | 181 | High | Editor | B | `AnnotationFormTests.AnnotationForm_TitleAndMarkdown_SaveAndPreviewSafely` | Title and Markdown description save, and the preview is safe |
| ANN-FRM-02 | 198 | Medium | Editor | B | `AnnotationFormTests.AnnotationForm_FieldLimits_BlockSave` | Field limits show their messages and block Save |
| ANN-FRM-03 | 200 | Medium | Editor | B | `AnnotationFormTests.AnnotationForm_DisabledAndDates_DriveStatus` | Disabled switch and validity dates drive the status |
| ANN-FRM-04 | 202 | Medium | Editor | B | `AnnotationFormTests.AnnotationForm_JsonAndYaml_EditAndGuardKeyFields` | JSON and YAML modes edit the annotation and guard the key fields |
| ANN-FRM-05 | 205 | Medium | Editor | B | `AnnotationFormTests.AnnotationForm_Labels_CreateReuseAndFilter` | Labels: create one, reuse it on a second annotation and filter by it |
| ANN-FRM-06 | 207 | Medium | Editor | B | `AnnotationFormTests.AnnotationForm_Values_TypedRowsAndDuplicates` | Values editor: typed rows, duplicate keys and Edit all as JSON |
| ANN-FRM-07 | 218 | Medium | Editor | B | `AnnotationFormTests.AnnotationForm_UnitFields_TypeDefinitionAndCronHint` | Unit fields: unit type, definition and the Cron hint |
| ANN-FRM-08 | 219 | High | Editor | B | `AnnotationFormTests.AnnotationForm_UnsavedChanges_RevertKeepOrDiscard` | Unsaved changes: Revert, Keep editing and Discard |
| ANN-DEL-01 | 220 | High | Editor | B | `AnnotationDeleteTests.AnnotationDelete_WithDescendants_UndoRestores` | Delete from the detail with descendants, then Undo |
| ANN-DEL-02 | 222 | Medium | Editor | B | `AnnotationDeleteTests.AnnotationDelete_DeletedDetail_RestoresFromBanner` | A deleted key's detail shows the banner and restores with or without descendants |
| ANN-ROL-01 | 223 | High | Reader | B | `AnnotationRoleTests.AnnotationAccess_Reader_HasNoCreateActions` | A Reader gets no create actions, not even in an empty view |
| ANN-ROL-02 | 225 | Medium | Contributor | B | `AnnotationRoleTests.AnnotationAccess_Contributor_CreatesEditsDeletes` | Exactly a Contributor can create, edit and delete |
| ANN-ROL-03 | 226 | Medium | Reader | M | `AnnotationRoleTests.AnnotationAccess_Reader_SeesDetailReadOnly` | A Reader sees an existing annotation read-only, without a bulk bar |

#### Configurations

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| CFG-CRE-01 | 3 (existing) | Elementary | Editor | B | `ConfigurationCreateTests.Configurations_SetDocument_EffectiveShowsIt` | Create empty, save a JSON document, and the Effective tab shows it |
| CFG-CRE-02 | 63 | High | Editor | B | `ConfigurationCreateTests.Configurations_NewDialog_OffersOnlyKeysWithoutConfiguration` | New configuration offers only annotations without their own configuration |
| CFG-CRE-03 | 74 | High | Owner | B,P | `ConfigurationCreateTests.Configurations_StartFromTemplate_CopiesTypeTemplate` | Start from template copies the type template, and Create empty still inherits it |
| CFG-CRE-04 | 81 | High | Editor | B | `ConfigurationCreateTests.Configurations_DeleteOwn_NeedsTypedKey` | Delete own needs the typed key and returns to the empty state |
| CFG-EDT-01 | 83 | High | Editor | B | `ConfigurationEditTests.Configurations_UnsavedChanges_AskBeforeLeaving` | Unsaved changes ask before leaving, and Revert drops them |
| CFG-EDT-02 | 95 | Medium | Editor | B | `ConfigurationEditTests.Configurations_SyntaxError_BlocksSaveAndFormatSwitch` | A syntax error disables Save and blocks the format switch |
| CFG-EDT-03 | 109 | Medium | Editor | B | `ConfigurationEditTests.Configurations_YamlMode_SavesAndKeepsFormat` | YAML mode edits and saves the document, and the format stays in the URL |
| CFG-EDT-04 | 123 | High | Editor | B | `ConfigurationEditTests.Configurations_Versions_CompareShowsDiff` | Versions lists every save and compares two versions |
| CFG-CON-01 | 125 | High | Editor | B | `ConfigurationConflictTests.Configurations_ConcurrentChange_OverwriteSavesMine` | A concurrent change of the same value opens the conflict dialog, and Overwrite saves mine |
| CFG-CON-02 | 140 | Medium | Editor | B | `ConfigurationConflictTests.Configurations_ConcurrentChange_ReloadTakesTheirs` | Reload, discard mine takes the stored version, and Copy mine copies mine |
| CFG-CON-03 | 150 | Low | Editor | B | `ConfigurationConflictTests.Configurations_ConcurrentChangeElsewhere_MergesWithoutConflict` | A concurrent change of another value merges without a conflict |
| CFG-SCH-01 | 159 | High | Editor | B | `ConfigurationSchemaFormTests.Configurations_SchemaForm_SavesValue` | The form follows the annotation's schema, and the Schema tab lists it |
| CFG-SCH-02 | 169 | High | Editor | B | `ConfigurationSchemaFormTests.Configurations_SchemaViolation_SaveAnywayStoresIt` | A schema violation lists the errors, and Save anyway stores the document |
| CFG-SCH-03 | 174 | Medium | Editor | B | `ConfigurationSchemaFormTests.Configurations_InheritedValue_OverrideAndReset` | An inherited value shows as a placeholder, with Override and Reset to inherited |
| CFG-SCH-04 | 185 | Medium | Editor | B | `ConfigurationSchemaFormTests.Configurations_FormListsAndMaps_SaveNestedValues` | Lists and maps of the form add nested values |
| CFG-INH-01 | 187 | High | Editor | B | `ConfigurationInheritanceTests.Configurations_Effective_MergesParentConfiguration` | A child's Effective merges the parent's configuration and keeps it after Delete own |
| CFG-INH-02 | 192 | Medium | Editor | B | `ConfigurationInheritanceTests.Configurations_Composition_ShowsStepOfEachLine` | Composition shows the step that set each line and links the parent |
| CFG-INH-03 | 210 | Medium | Editor | B | `ConfigurationInheritanceTests.Configurations_BreadcrumbAndLineage_OpenParent` | The breadcrumb and Lineage open the parent's configuration, and Annotation opens the annotation |
| CFG-BND-01 | 214 | High | Editor | B | `ConfigurationBindingTests.Configurations_Resolved_RevealShowsEvaluatedBindings` | Resolved is hidden until Reveal and shows the evaluated bindings |
| CFG-BND-02 | 221 | Medium | Editor | B | `ConfigurationBindingTests.Configurations_Resolved_ShowsBindingError` | A binding that cannot be evaluated shows the binding error |
| CFG-BND-03 | 224 | Medium | Editor | B | `ConfigurationBindingTests.Configurations_JsonEditor_CompletesAndExplainsBindings` | The JSON editor completes @config paths, explains them on hover, and Bindings filters the reference |
| CFG-BND-04 | 227 | Medium | Editor | B | `ConfigurationBindingTests.Configurations_FormBinding_UseAndConvertToValue` | fx turns a form field into a binding, Use fills it, and Convert to value turns it back |
| CFG-NAV-01 | 228 | High | Editor | B | `ConfigurationNavigationTests.Configurations_List_ShowsVersionAndOpensDetail` | A saved configuration is listed with its version and author, and its row opens the detail |
| CFG-NAV-02 | 229 | Low | Editor | B | `ConfigurationNavigationTests.Configurations_Tabs_KeepFilterAndCloseToList` | The tabs keep the filter, and Close returns to the list |
| CFG-NAV-03 | 230 | Medium | Editor | B,D | `ConfigurationNavigationTests.Configurations_Section_ReopensLastConfiguration` | Returning to Configurations reopens the last configuration and tab |
| CFG-ROL-01 | 231 | Medium | Contributor | B | `ConfigurationRoleTests.Configurations_Contributor_EditsWithoutSecrets` | A Contributor creates and saves a configuration, and Resolved leaves secret values out |
| CFG-ROL-02 | 232 | Medium | Reader | B | `ConfigurationRoleTests.Configurations_Reader_HasNoCreateActions` | A Reader gets no New and no create actions |
| CFG-ROL-03 | 233 | Medium | Reader | M | `ConfigurationRoleTests.Configurations_Reader_SeesConfigurationReadOnly` | A Reader sees an existing configuration read-only |

#### Modeling

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| MOD-TAB-01 | 14 | High | Admin | B | `ModelingTabsTests.Modeling_OpenTabs_EachTabOpensItsPage` | Modeling opens on Schemas and every tab opens its own page |
| MOD-TAB-02 | 40 | Low | Editor | B | `ModelingTabsTests.Modeling_OldUrls_RedirectToModelingTabs` | Old URLs of the merged pages redirect to their Modeling tabs |
| MOD-TAB-03 | 42 | High | Reader | B | `ModelingTabsTests.Modeling_Reader_GetsContributorsOnlyLock` | A Reader gets the Contributors only lock on every Modeling URL |
| MOD-TAB-04 | 43 | High | Contributor | B | `ModelingTabsTests.Modeling_Contributor_GetsNoAdministratorFeatures` | A Contributor gets four tabs, the Administrators only lock and no Delete permanently |
| MOD-TAB-05 | 44 | Medium | Editor | B | `ModelingTabsTests.Modeling_ReturnFromOtherSection_RestoresLastTab` | Modeling returns to the last tab, detail and query after another section |
| MOD-TAB-06 | 46 | Medium | Editor | B,D | `ModelingTabsTests.Modeling_LeaveDirtySchemaEditor_AsksToDiscard` | A dirty schema editor asks Discard changes? before another tab opens |
| MOD-SCH-01 | 92 | Elementary | Editor | B | `ModelingSchemaTests.Schemas_CreateAnnotationSchema_ListsVersionOne` | Create an annotation schema for a new annotation |
| MOD-SCH-02 | 93 | High | Editor | B | `ModelingSchemaTests.Schemas_NewSchemaDialog_OpensDescendantTypeEditor` | New schema offers the kinds and only the types that can descend, then opens the editor |
| MOD-SCH-03 | 96 | High | Owner | B,P | `ModelingSchemaTests.Schemas_CreateAndDeleteTypeSchema_UpdatesTypeRow` | Create and delete a type schema |
| MOD-SCH-04 | 99 | High | Editor | B | `ModelingSchemaTests.Schemas_InvalidDocument_BlocksSaveUntilFixed` | Meta-schema and syntax errors block saving a schema, and Revert restores it |
| MOD-SCH-05 | 101 | High | Editor | B | `ModelingSchemaTests.Schemas_BreakingChange_SaveAnywayForcesIt` | A schema that breaks a configuration lists it, and Save anyway forces it |
| MOD-SCH-06 | 103 | Medium | Editor | B | `ModelingSchemaTests.Schemas_FormPreview_RendersObjectSchema` | The form preview renders the schema and keeps sample data |
| MOD-SCH-07 | 105 | Medium | Editor | B | `ModelingSchemaTests.Schemas_VersionsAndDelete_ShowHistoryThenNone` | Schema versions show the history and a comparison, and a typed delete ends them |
| MOD-TPL-01 | 147 | High | Owner | B,P | `ModelingTemplateTests.Templates_CreateEditAndDelete_KeepsVersions` | Create, edit, version and delete a template |
| MOD-TPL-02 | 149 | Medium | Owner | B,P | `ModelingTemplateTests.Templates_ConcurrentChange_OpensConflictDialog` | A concurrent template change opens the conflict dialog |
| MOD-EXI-01 | 151 | High | Admin | B | `ModelingExportImportTests.Export_FolderOfJsonFiles_ChecksBackInUnchanged` | Export the view as a folder of JSON files and check it back in |
| MOD-EXI-02 | 153 | Medium | Admin | B | `ModelingExportImportTests.Export_KindsAndGitFormat_ShapeTheArchive` | Export only some kinds, require one kind, and export a Git repository |
| MOD-EXI-03 | 155 | High | Admin | B | `ModelingExportImportTests.Import_CheckThenImportNow_CreatesAnnotation` | Check an archive with a new annotation, then Import now |
| MOD-EXI-04 | 156 | Medium | Admin | B | `ModelingExportImportTests.Import_SkipExisting_KeepsItAfterReload` | Skip keeps an existing document, and the result shows again after a reload |
| MOD-EXI-05 | 171 | Medium | Admin | B | `ModelingExportImportTests.Import_FaultyArchive_ReportsEveryProblem` | A file that is not a zip is refused, and excluded, ignored and invalid files are reported |
| MOD-EXI-06 | 172 | Medium | Admin | B | `ModelingExportImportTests.Import_BreakingSchema_FailsUnlessForced` | A schema that breaks a configuration fails the import unless forced |
| MOD-EXI-07 | 173 | Low | Admin | B | `ModelingExportImportTests.Import_EndedElsewhere_ToastShowsResult` | The toaster reports an import that ended after leaving the page |
| MOD-EXI-08 | 175 | High | Admin | B | `ModelingExportImportTests.Import_OverwriteExisting_UpdatesDocument` | Overwrite, the default, updates an existing document |
| MOD-DEL-01 | 196 | High | Editor | B | `ModelingDeletedTests.Deleted_ShowCascaded_ListsDescendants` | Deleted lists the root of a deletion and Show cascaded adds its descendants |
| MOD-DEL-02 | 199 | High | Editor | B | `ModelingDeletedTests.Deleted_Restore_WithAndWithoutDescendants` | Restore a deletion with and without its descendants |
| MOD-DEL-03 | 201 | Low | Editor | B | `ModelingDeletedTests.Deleted_BulkRestore_RestoresSelection` | Bulk restore of selected deletions |
| MOD-DEL-04 | 203 | Medium | Admin | B | `ModelingDeletedTests.Deleted_DeletePermanently_NeedsTypedConfirmation` | Delete permanently needs the typed key, or the typed count for several |
| MOD-CMP-01 | 208 | Medium | Owner | B,P | `ModelingCompareTests.CompareViews_OtherView_PreselectedAndDiffed` | Compare views is disabled without another view, then preselects it and shows the difference |

#### Graph

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| GRA-OVR-01 | 16 | Elementary | Editor | B | `GraphOverviewTests.Graph_FocusOnCreatedTree_OpensNodeByKeyboard` | Graph focused on a created tree shows its neighbourhood and opens a node by keyboard |
| GRA-OVR-02 | 56 | Medium | Editor | B | `GraphOverviewTests.Graph_ArrowKeys_MoveAlongLinksAndOpenNode` | Arrow keys move along the links, Enter opens the annotation and Escape closes it |
| GRA-OVR-03 | 57 | Medium | Editor | B,Ph | `GraphOverviewTests.Graph_PhoneLayout_CollapsesLegendAndCoversGraph` | On a phone the legend is a Types popover, the toolbar is icon-only and a node's detail covers the graph |
| GRA-LEG-01 | 58 | High | Editor | B | `GraphLegendTests.GraphLegend_HideTypes_RemovesNodesUntilShownAgain` | Hiding types removes their nodes, survives a catalog reload, and the legend brings them back |
| GRA-LEG-02 | 59 | Low | Editor | B,D | `GraphLegendTests.GraphLegend_EnterOnChip_TogglesTypeWithNodeOpen` | Enter on a focused legend chip toggles its type while a node is open |
| GRA-FOC-01 | 60 | High | Editor | B | `GraphFocusTests.GraphFocus_FocusAndDepth_ChangeNeighbourhood` | Focus starts from the last opened annotation, the depth changes the neighbourhood and Show everything leaves it |
| GRA-FOC-02 | 62 | Medium | Editor | B | `GraphFocusTests.GraphFocus_Filter_AddsMatchesAndKeepsWholeView` | The filter adds its matches to the focus and keeps every node of the whole view |
| GRA-TBR-01 | 91 | Medium | Editor | B | `GraphToolbarTests.GraphToolbar_LayoutAndExport_KeepNodesAndDownloadPng` | Layout, Fit and Reheat redraw the graph without changing its annotations, and Export PNG downloads it |
| GRA-TBR-02 | 94 | High | Editor | B | `GraphToolbarTests.GraphToolbar_OpenAsList_ShowsDrawnAnnotationsInGrid` | Open as list shows the drawn annotations in the grid |
| GRA-DET-01 | 97 | High | Editor | B | `GraphNodeDetailTests.GraphDetail_OpenByUrl_KeepsGraphQueryOnTabsAndClose` | A node's detail opens beside the graph, and its tabs, breadcrumb and close keep the graph's query |
| GRA-DET-02 | 98 | Medium | Editor | B | `GraphNodeDetailTests.GraphDetail_LineageAndShowInList_SwitchListAndGraph` | Lineage, Show in list and Show in graph move between the graph and the list |
| GRA-DET-03 | 100 | Medium | Editor | B | `GraphNodeDetailTests.GraphDetail_DeleteAndUndo_RemovesAndRestoresNode` | Deleting a node from its detail removes it from the graph, and Undo brings it back |
| GRA-DET-04 | 102 | Low | Any | B | `GraphNodeDetailTests.GraphDetail_UnknownKeyAndOldTabs_ShowNotFoundAndRedirect` | An unknown node shows Annotation not found, and the old tab links of a node redirect to its configuration with their query |
| GRA-CRE-01 | 128 | High | Editor | B | `GraphCreateTests.GraphCreate_NewInTopBar_OpensAnnotationInGraph` | New in the Graph top bar creates an annotation and opens it in the graph |
| GRA-CRE-02 | 130 | High | Owner | B,P | `GraphCreateTests.GraphCreate_BulkCreateInEmptyProject_DrawsTree` | The empty graph of a new project offers New annotation and Bulk create, and Bulk create draws the tree |
| GRA-CRE-03 | 133 | Medium | Reader | B | `GraphCreateTests.GraphCreate_Reader_HasNoCreateActions` | A Reader has no create actions on Graph |
| GRA-CRE-04 | 136 | Low | Editor | B | `GraphCreateTests.GraphCreate_DialogLeftOpen_DoesNotReopenOnAnnotations` | A create dialog left open on Graph does not reopen on Annotations after Back |
| GRA-MEM-01 | 138 | Medium | Editor | B | `GraphMemoryTests.GraphMemory_SidebarReturn_RestoresStateUntilReload` | Coming back to Graph through the navigation restores its URL state and hidden types until a reload |

#### Key-Values

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| KV-CRE-01 | 4 (existing) | Elementary | Editor | B | `KeyValueCreateTests.KeyValues_CreateAndRead` | Create a public key value and read it back |
| KV-CRE-02 | 15 | High | Editor | B | `KeyValueCreateTests.KeyValues_NewKeyInvalidInput_ShowsFieldErrors` | New key validates the key grammar and the value size |
| KV-CRE-03 | 45 | Medium | Editor | B | `KeyValueCreateTests.KeyValues_NewKeyExistingName_ShowsKeyError` | New key with the name of an existing key shows a field error |
| KV-CRE-04 | 47 | Medium | Secrets | B | `KeyValueCreateTests.KeyValues_NewSecretReference_ShowsReferenceBadge` | A secret reference is validated and shows the Reference badge |
| KV-DET-01 | 48 | High | Editor | B | `KeyValueDetailTests.KeyValues_EditPublicValue_SavesNewVersions` | Edit a public value in place and save it |
| KV-DET-02 | 49 | Medium | Editor | B | `KeyValueDetailTests.KeyValues_BindingSource_ChangesCopiedBinding` | The metadata and the binding source of a key |
| KV-DET-03 | 51 | Medium | Editor | B | `KeyValueDetailTests.KeyValues_UnsavedChanges_AskBeforeVersions` | Unsaved changes ask before switching to Versions |
| KV-DET-04 | 53 | High | Editor | B | `KeyValueDetailTests.KeyValues_ConcurrentChange_OffersReloadOrOverwrite` | A concurrent change offers Reload or Overwrite |
| KV-SEC-01 | 84 | High | Secrets | B | `KeyValueSecretTests.KeyValues_StandardSecret_RevealsCopiesAndHides` | Create a Standard secret, reveal, copy and hide it |
| KV-SEC-02 | 85 | Low | Secrets | B | `KeyValueSecretTests.KeyValues_RevealedSecret_MasksAfterSixtySeconds` | A revealed secret is masked again after 60 seconds |
| KV-SEC-03 | 86 | High | Secrets | B | `KeyValueSecretTests.KeyValues_SetNewSecretValue_SavesAndMakesPublic` | Set a new value for a secret, then make it public |
| KV-SEC-04 | 87 | Medium | Owner | B,P | `KeyValueSecretTests.KeyValues_ChangeProtectionToHsm_KeepsValue` | Change the protection of a secret to HSM |
| KV-SEC-05 | 89 | Medium | Owner | B,P | `KeyValueSecretTests.KeyValues_HyokValue_EncryptsInBrowser` | A HYOK value is encrypted in the browser for the recipients |
| KV-SEC-06 | 90 | Medium | Owner | B,D,P | `KeyValueSecretTests.KeyValues_BulkChangeProtection_KeepsFailedSelected` | Bulk Change protection keeps the keys it could not change selected |
| KV-VER-01 | 126 | Medium | Editor | B | `KeyValueVersionTests.KeyValues_JsonVersions_LoadOnDemandAndCompare` | A JSON value edited in the code editor: versions load on demand, View and Compare |
| KV-VER-02 | 127 | Medium | Secrets | B | `KeyValueVersionTests.KeyValues_SecretVersions_RevealAndCompare` | Reveal and compare secret versions |
| KV-LST-01 | 129 | Medium | Owner | B,P | `KeyValueListTests.KeyValues_SearchAndLevelFilter_NarrowTheList` | Search, level filter and the query kept by the detail |
| KV-LST-02 | 131 | Medium | Owner | B,D,P | `KeyValueListTests.KeyValues_RowMenu_CopiesAndOpens` | The row menu copies the key and the binding and opens the detail |
| KV-LST-03 | 134 | Low | Owner | B,D,P | `KeyValueListTests.KeyValues_SwitchProject_DropsKeySelectionAndOptIn` | Switching the project drops the key, the selection and the secrets opt-in |
| KV-LIF-01 | 164 | High | Owner | B,D,P | `KeyValueLifecycleTests.KeyValues_DeleteFromRowMenu_RestoresFromDetail` | Delete a key from the row menu and restore it from the Deleted keys |
| KV-LIF-02 | 165 | Medium | Owner | B,D,P | `KeyValueLifecycleTests.KeyValues_DeletePermanentlyAndPurge_RemoveForGood` | Remove keys for good with Delete permanently and Purge |
| KV-LIF-03 | 167 | Medium | Owner | B,D,P | `KeyValueLifecycleTests.KeyValues_BulkDeleteAndRestore_ReturnsKeys` | Bulk delete and bulk restore of selected keys |
| KV-LIF-04 | 168 | Low | Owner | B,D,P | `KeyValueLifecycleTests.KeyValues_NewKeyOverDeleted_ContinuesVersions` | New key over a deleted key continues its versions |
| KV-IEX-01 | 189 | High | Secrets | B | `KeyValueImportExportTests.KeyValues_ImportDotEnv_MarksPasswordAsSecret` | Import .env lines and mark the password key as a secret |
| KV-IEX-02 | 190 | Medium | Secrets | B | `KeyValueImportExportTests.KeyValues_ImportDocumentThenFlat_PlansEachAction` | Import a key/value document from a file, then a flat JSON plan of create, update and unchanged |
| KV-IEX-03 | 191 | Medium | Editor | B | `KeyValueImportExportTests.KeyValues_ImportUnreadableText_StaysOnSource` | Import refuses unreadable text and invalid keys |
| KV-IEX-04 | 193 | Medium | Owner | B,P | `KeyValueImportExportTests.KeyValues_ImportReplaceUnderPrefix_UndoRestores` | Replace under a prefix deletes the unlisted keys; Undo deletes restores them |
| KV-IEX-05 | 194 | High | Secrets | B | `KeyValueImportExportTests.KeyValues_ExportWithoutSecrets_LeavesSecretsOut` | Export .env, flat JSON and the key/value document without secret values |
| KV-IEX-06 | 195 | Medium | Owner | B,P | `KeyValueImportExportTests.KeyValues_ExportWithSecrets_IncludesValues` | Export with secret values on request |
| KV-ROL-01 | 212 | Medium | Reader | B | `KeyValueRoleTests.KeyValues_Reader_SeesReadOnlyStore` | A Reader sees Values store read-only and can export it |
| KV-ROL-02 | 213 | Medium | Contributor | B | `KeyValueRoleTests.KeyValues_Contributor_CreatesPublicKeysOnly` | A Contributor creates and edits only public keys |

#### Settings

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| SET-PRJ-01 | 17 | Elementary | Admin | B | `SettingsProjectTests.Settings_OpenAsAdministrator_ShowsEverySection` | Settings page shows every section of the project for an Administrator |
| SET-PRJ-02 | 39 | High | Owner | B,P | `SettingsProjectTests.Settings_SaveDescription_ShowsOnOrganizationOverview` | Administrator changes the project description and it shows on the organization overview |
| SET-PRJ-03 | 41 | High | Reader | B | `SettingsProjectTests.Settings_OpenAsReader_ShowsEverythingReadOnly` | A Reader sees the project settings read-only |
| SET-PRJ-04 | 61 | Medium | Contributor | B | `SettingsProjectTests.Settings_OpenAsContributor_ChangesOnlyBrowserSettings` | A Contributor changes the browser settings but not the project-wide ones |
| SET-BRW-01 | 65 | High | Editor | B | `SettingsBrowserTests.Settings_DefaultTimeZone_NewAnnotationStartsInIt` | The default time zone is kept in this browser, and new annotations start and are saved in it |
| SET-BRW-02 | 78 | High | Editor | B | `SettingsBrowserTests.Settings_DateFormat_ChangesShownDates` | The project's date format changes how dates are shown |
| SET-BRW-03 | 88 | High | Editor | B | `SettingsBrowserTests.Settings_CustomizeType_ShowsOnTemplates` | The project renames, recolors and re-icons an annotation type |
| SET-BRW-04 | 108 | Medium | Editor | B | `SettingsBrowserTests.Settings_ResetTypes_RestoresBuiltInNames` | A type's plural needs a name, and one type or all types can be reset |
| SET-PRF-01 | 118 | Medium | Any | B | `SettingsPreferencesTests.Preferences_ChangeAndReset_RestoresDefaults` | Preferences are kept in this browser and 'Reset preferences' restores the defaults |
| SET-PRF-02 | 124 | Medium | Editor | B | `SettingsPreferencesTests.Preferences_PersonalDateFormat_WinsOverProject` | A personal date format in Preferences wins over the project's |
| SET-PRF-03 | 137 | Medium | Editor | B | `SettingsPreferencesTests.Preferences_PersonalTypeName_WinsOverProject` | Personal type names in Preferences win over the project's on Templates and Annotations |
| SET-MAC-01 | 157 | High | Owner | B,P | `SettingsMachineAuthenticationTests.MachinePolicy_ChangeKindsAndLifetime_IsKept` | Administrator changes the credential kinds, the default kind and the certificate lifetime |
| SET-MAC-02 | 158 | Medium | Any | B | `SettingsMachineAuthenticationTests.CertificateAuthority_Download_SavesPem` | Anyone downloads the CA certificate as PEM |
| SET-SEC-01 | 166 | High | Owner | B,P | `SettingsSecretsPolicyTests.SecretsPolicy_TurnOffMtls_AsksFirstAndIsKept` | Turning off the mTLS rule asks first; Cancel keeps it and the confirmed change is kept |
| SET-SEC-02 | 170 | High | Owner | B,P | `SettingsSecretsPolicyTests.SecretsPolicy_DisableLevelInUse_ShowsCountsPerView` | Disabling a level that keys still use shows the counts per view |
| SET-SEC-03 | 179 | Medium | Owner | B,P | `SettingsSecretsPolicyTests.SecretsPolicy_AllowByokUnverified_IsRefused` | A customer key can be saved, but BYOK cannot be allowed before it is verified |
| SET-SEC-04 | 186 | High | Owner | B,P | `SettingsSecretsPolicyTests.SecretsPolicy_AllowHyok_ProtectsLastRecipient` | HYOK is allowed once a recipient exists, becomes the default level and protects its last recipient |
| SET-SEC-05 | 188 | Low | Owner | B,P | `SettingsSecretsPolicyTests.SecretsPolicy_SaveStalePolicy_IsRefused` | Saving a secrets policy that another writer changed meanwhile is refused |
| SET-KEY-01 | 197 | Medium | Owner | B,P | `SettingsKeysRecipientsTests.SecretKeys_RotateStandardKey_ShowsNewVersion` | Administrator rotates the Standard key after confirming |
| SET-KEY-02 | 211 | High | Owner | B,P | `SettingsKeysRecipientsTests.Recipients_AddDisableRemove_RefusesInvalidKeys` | Administrator adds, disables, enables and removes a recipient, and invalid keys are refused |
| SET-KEY-03 | 215 | Medium | Owner | B,P | `SettingsKeysRecipientsTests.SecretKeys_AllowHsm_CreatesAndRotatesHsmKey` | Allowing HSM creates the HSM key, which an Administrator rotates |

#### Monitoring

| Reference | Testiny | Priority | Role | Mode | Test | Title |
|---|---|---|---|---|---|---|
| MON-PAG-01 | 7 | Elementary | Admin | B | `MonitorTests.Monitor_SwitchTabs_ShowsUsageAndAudit` | Monitor opens on Usage and switches to Audit through the tabs |
| MON-PAG-02 | 9 | High | Reader | B | `MonitorTests.Monitor_OpenAsReader_ShowsAdministratorsOnly` | Monitor is for Administrators only |
| MON-PAG-03 | 10 | Medium | Admin | B | `MonitorTests.MonitorUsage_ChangeRange_ShowsReportOfRange` | The usage report follows the chosen date range and refuses a reversed one |
| MON-PAG-04 | 11 | Medium | Admin | B | `MonitorTests.Monitor_OpenOldLinks_OpensTheirTabs` | Old Usage and Audit links open the Monitor tabs |
| MON-AUD-01 | 25 | High | Admin | B | `MonitorAuditTests.MonitorAudit_CreateAndRevealSecret_RecordsBothEvents` | Creating and revealing a secret is recorded in the audit trail |
| MON-AUD-02 | 29 | Medium | Admin | B | `MonitorAuditTests.MonitorAudit_ApplyFilters_NarrowsEvents` | Audit filters narrow the events by operation, view and dates |
| MON-AUD-03 | 32 | High | Owner | B,P | `MonitorAuditTests.MonitorAudit_ProjectSecretChanges_AreRecorded` | Project-wide secret changes and an export with secret values are recorded in the audit trail |
