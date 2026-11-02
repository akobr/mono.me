# Test Cases of the Admin UI

The functional test cases of the admin UI in Testiny project `2SP` (id 1) and the automated test of each, in `src/Platform/UI.Admin.AutomationTests/src/Tests/`. Every case has type Functional, status Ready for Test and automation Automated. Each Testiny folder is a folder of `Tests/`, and each sub-folder is a test class. The case's steps are in Testiny and in the plan [2026-11-02 Initial Test Cases of the Admin UI and Their Automation](specs/2026-11-02%20Initial%20Test%20Cases%20of%20the%20Admin%20UI%20and%20Their%20Automation.md), which also lists what is not automated and why. How to run and write the tests: [automation-tests.md](automation-tests.md).

- **Reference**: `<section>-<sub-folder>-<n>`; it starts the Testiny precondition and the comment above the test.
- **Priority**: Elementary (the section's PR-gate test, `[Elementary]`), High, Medium or Low.
- **Role** (`[UserRole]`):
  - Any: any pool user.
  - Editor: Contributor or higher.
  - Admin: Administrator or Owner.
  - Secrets: ContributorWithSecrets or higher.
  - Owner.
  - Contributor and Reader: exactly that role, through `Target:RoleProjects`.
- **Mode**:
  - B: mock and deployed targets.
  - D: desktop only (`[DesktopOnly]`).
  - Ph: phone only (`[PhoneOnly]`).
  - M: mock only (`[MockOnly]`).
  - P: in a disposable project (`[DisposableProject]`).

233 cases:

| Section | Folder | Cases |
|---|---|---|
| [Access](#access) | `Tests/Access` | 33 |
| [Organizations and Projects](#organizations-and-projects) | `Tests/Projects` | 10 |
| [Navigation](#navigation) | `Tests/Navigation` | 23 |
| [Annotations](#annotations) | `Tests/Annotations` | 34 |
| [Configurations](#configurations) | `Tests/Configurations` | 28 |
| [Modeling](#modeling) | `Tests/Modeling` | 28 |
| [Graph](#graph) | `Tests/Graph` | 18 |
| [Key-Values](#key-values) | `Tests/KeyValues` | 31 |
| [Settings](#settings) | `Tests/Settings` | 21 |
| [Monitoring](#monitoring) | `Tests/Monitoring` | 7 |

## Access

### Sign In and Account (`SignInTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ACC-SGN-01 | 1 | A signed-in user sees the organizations and the target project | Elementary | Any | B | `SignIn_ShowsOrganizations` |
| ACC-SGN-02 | 12 | Signing out shows the sign-in page, and Continue with AuthKit signs in again | High | Any | B | `SignIn_SignOutAndContinue_ReturnsToLastProject` |
| ACC-SGN-03 | 13 | A signed-out deep link signs in and returns to the requested page | High | Any | B | `SignIn_SignedOutDeepLink_ReturnsToThePage` |
| ACC-SGN-04 | 19 | The login, callback and unsupported pages handle the return path, sign-in errors and the configured state | Medium | Any | B | `SignIn_LoginPage_HandlesReturnPathAndErrors` |
| ACC-SGN-05 | 20 | The Account page shows the profile and the memberships, and a membership opens its project | Medium | Any | B | `Account_Memberships_OpenTheProject` |
| ACC-SGN-06 | 21 | A new user creates the first organization and project on onboarding | Medium | Any | M | `Onboarding_NewUser_CreatesFirstOrganization` |

### Members (`MemberTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ACC-MEM-01 | 50 | The project Members page lists the members, locks the own role and highlights a member | High | Admin | B | `Members_ProjectPage_LocksOwnRoleAndHighlights` |
| ACC-MEM-02 | 52 | The only Owner of a new project can be neither demoted nor removed | Medium | Owner | B,P | `Members_OnlyOwner_CannotBeDemotedOrRemoved` |
| ACC-MEM-03 | 54 | An Owner changes the role of a member and removes another member | Medium | Owner | M | `Members_ChangeRoleAndRemove_UpdatesTable` |
| ACC-MEM-04 | 55 | Below Administrator the Members page offers no Invite and no access | Medium | Contributor | B | `Members_Contributor_ShowsNoAccess` |

### Invitations (`InvitationTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ACC-INV-01 | 77 | Invite one person, resend and copy the link, then revoke the invitation | High | Admin | B | `Invitations_InviteResendRevoke_EndsRevoked` |
| ACC-INV-02 | 79 | Several typed addresses get one invitation each, with one role and expiry | Medium | Owner | B | `Invitations_TypedList_SendsOneRoleAndExpiry` |
| ACC-INV-03 | 80 | A list pasted from a mail client invites people to the organization | Medium | Owner | B | `Invitations_PastedListToOrganization_SendsEach` |
| ACC-INV-04 | 82 | Missing, invalid and already invited addresses are refused | High | Admin | B | `Invitations_InvalidAndPendingAddresses_AreRefused` |

### My Invitations (`MyInvitationTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ACC-MYI-01 | 104 | A self-invitation is declined on Invitations, and a second one is accepted | Medium | Admin | B | `MyInvitations_SelfInvitation_DeclineThenAccept` |
| ACC-MYI-02 | 106 | An invited user without an account joins through Invitations | Medium | Any | M | `MyInvitations_InvitedNewUser_JoinsProject` |
| ACC-MYI-03 | 107 | The invitation landing asks a signed-out visitor to continue to the sign-in | Low | Any | B | `MyInvitations_LandingSignedOut_ContinuesToSignIn` |

### Machines (`MachineTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ACC-MCH-01 | 132 | Create an API key machine, store its one-time credentials and delete it | High | Editor | B | `Machines_CreateApiKey_ShowsCredentialsOnce` |
| ACC-MCH-02 | 135 | Reset replaces the API key, and Delete needs the exact id | High | Editor | B | `Machines_ResetApiKey_ShowsNewKey` |
| ACC-MCH-03 | 139 | Presets and scope rules of New machine | Medium | Admin | B | `Machines_Presets_ReplaceScopes` |
| ACC-MCH-04 | 141 | A machine with management and secrets scopes, and its detail panel | Medium | Admin | B | `Machines_ManagementAndSecretsScopes_ShowOnDetail` |
| ACC-MCH-05 | 160 | A Contributor gives neither management nor secrets scopes | High | Contributor | B | `Machines_Contributor_GivesRuntimeScopesOnly` |
| ACC-MCH-06 | 161 | A Contributor can neither reset nor delete machines with elevated scopes | Medium | Contributor | M | `Machines_Contributor_CannotResetElevatedMachines` |
| ACC-MCH-07 | 162 | A machine restricted to an annotation | Medium | Owner | B,P | `Machines_AnnotationRestriction_ShowsKey` |
| ACC-MCH-08 | 163 | A Reader has no access to Machines | Low | Reader | B | `Machines_Reader_ShowsNoAccess` |

### Machine Certificates (`MachineCertificateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ACC-CRT-01 | 182 | A Certificate machine shows its certificate once and downloads it | Medium | Owner | B,P | `Certificates_CertificateMachine_DownloadsPfx` |
| ACC-CRT-02 | 183 | A CertificateAndApiKey machine uses a shared certificate, and its reset issues only a new API key | Medium | Owner | B,P | `Certificates_SharedCertificateMachine_ResetsApiKeyOnly` |
| ACC-CRT-03 | 184 | Issue a shared certificate, download it and revoke it with its thumbprint prefix | High | Owner | B,P | `Certificates_IssueSharedCertificate_DownloadsAndRevokes` |

### Federation (`FederationTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ACC-FED-01 | 204 | Add, edit and delete a trusted issuer from the GitHub template | High | Owner | B,P | `TrustedIssuers_AddEditDelete_UpdatesList` |
| ACC-FED-02 | 206 | Issuer templates fill the dialog, and invalid or taken names and an http issuer are refused | Medium | Owner | B,P | `TrustedIssuers_TemplatesAndValidation_GuideInput` |
| ACC-FED-03 | 209 | From no trusted issuer to a Federated machine with its setup | High | Owner | B,P | `Federation_NoIssuerToFederatedMachine_ShowsSetup` |
| ACC-FED-04 | 216 | Federation rules are set up and edited on the machine detail | Medium | Owner | B,P | `Federation_EditRules_SavesAndValidates` |
| ACC-FED-05 | 217 | The JWKS URI and Pasted JWKS key sources need their keys, and the Key source column shows them | Low | Owner | B,P | `TrustedIssuers_KeySources_RequireJwks` |

## Organizations and Projects

### Organizations (`OrganizationTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ORG-ORG-01 | 8 | New organization creates the organization with its first project and lists it | High | Any | B | `Organization_Create_ListsItWithFirstProject` |
| ORG-ORG-02 | 23 | New organization validates the names and rejects an existing project | Medium | Owner | B | `Organization_CreateInvalid_ShowsFieldErrors` |

### Projects (`ProjectTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ORG-PRJ-01 | 5 | New project opens its annotations and its Settings show the description | Elementary | Owner | B | `Project_Create_OpensSettings` |
| ORG-PRJ-02 | 26 | New project validates the name and rejects an existing project | Medium | Owner | B | `Project_CreateInvalid_ShowsFieldErrors` |
| ORG-PRJ-03 | 28 | A project member without an organization role cannot create projects | Medium | Reader | M | `Project_CreateWithoutOrganizationRole_IsDisabled` |

### Views (`ProjectViewTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ORG-VEW-01 | 31 | New view creates a view that Compare links to and the list opens | High | Owner | B,P | `Views_Create_LinksCompareAndOpensView` |
| ORG-VEW-02 | 34 | Register the discovered default view and edit its description | Medium | Owner | B,P | `Views_RegisterDefault_EditsDescription` |
| ORG-VEW-03 | 36 | New view validates the name and rejects an existing view | Medium | Admin | B | `Views_CreateInvalid_ShowsNameErrors` |
| ORG-VEW-04 | 37 | A Reader lists the views without Compare or managing actions | Medium | Reader | B | `Views_Reader_ListsWithoutActions` |
| ORG-VEW-05 | 38 | A Contributor compares views but cannot create, register or edit them | Low | Contributor | B | `Views_Contributor_ComparesWithoutManaging` |

## Navigation

### Navigation Menu (`NavigationMenuTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| NAV-MNU-01 | 6 | Navigation menu opens the pages of the view, the project and the app | Elementary | Any | B | `Navigation_MobileMenu_OpensPages` |
| NAV-MNU-02 | 22 | Pages outside the view keep the sections of the last view | High | Any | B | `Navigation_OtherPages_KeepLastViewSections` |
| NAV-MNU-03 | 24 | An Administrator's menu lists every entry without locks | High | Admin | B | `Navigation_AdministratorMenu_ListsEveryEntryUnlocked` |
| NAV-MNU-04 | 27 | A Contributor's menu leaves out Members, Monitor and Export & Import | Medium | Contributor | B | `Navigation_ContributorMenu_HidesAdministration` |
| NAV-MNU-05 | 30 | A Reader's menu locks the editing sections | Medium | Reader | B | `Navigation_ReaderMenu_LocksEditingSections` |
| NAV-MNU-06 | 33 | Unpinning the sidebar moves the menu behind Open navigation and is kept | Medium | Any | B,D | `Sidebar_Unpin_MovesMenuBehindButtonAndIsKept` |
| NAV-MNU-07 | 35 | The icon rail expands and collapses, and [ toggles the docked sidebar | Low | Any | B,D | `Sidebar_IconRail_ExpandsCollapsesAndToggles` |

### Context Switcher (`NavigationContextSwitcherTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| NAV-CTX-01 | 66 | The view switcher changes the view, keeps the section and marks the default view | High | Any | B,D | `ContextSwitcher_PickView_KeepsSection` |
| NAV-CTX-02 | 68 | The project switcher keeps the project section and the view section | High | Owner | B,P | `ContextSwitcher_PickProject_KeepsSection` |
| NAV-CTX-03 | 70 | The organization switcher lists the organizations and opens an overview | Medium | Any | B | `ContextSwitcher_PickOrganization_OpensOverview` |

### Command Palette (`NavigationCommandPaletteTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| NAV-PAL-01 | 72 | The command palette opens pages and projects, also from a text box | High | Any | B | `CommandPalette_PagesAndProjects_Navigate` |
| NAV-PAL-02 | 75 | The palette's editor actions create an annotation that the palette then finds and lists as recent | Medium | Editor | B | `CommandPalette_EditorActions_FindAndRecallAnnotation` |
| NAV-PAL-03 | 76 | The theme switches in the palette and on the Account page and is kept | Medium | Any | B | `Theme_SwitchInPaletteAndAccount_IsKept` |

### Keyboard Shortcuts (`NavigationShortcutTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| NAV-KEY-01 | 115 | The keyboard shortcuts help opens from ?, the user menu and the palette | Medium | Any | B | `Shortcuts_Help_OpensFromKeyUserMenuAndPalette` |
| NAV-KEY-02 | 116 | Go-to shortcuts open the sections of the last view and are ignored in text boxes | Medium | Any | B | `Shortcuts_GoTo_OpenSectionsOfLastView` |

### Routing (`NavigationRoutingTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| NAV-RTE-01 | 117 | An unknown address shows Page not found, and Go home opens the last view | Medium | Any | B | `Routing_UnknownAddress_ShowsPageNotFound` |
| NAV-RTE-02 | 119 | An organization or a project outside the memberships shows No access | High | Any | B | `Routing_ForeignContext_ShowsNoAccess` |
| NAV-RTE-03 | 120 | Home, the project address and the app root open the last context | Medium | Any | B | `Routing_Home_OpensLastContext` |
| NAV-RTE-04 | 121 | Each section of a view comes back with its own filter until a reload | Medium | Any | B | `PageMemory_ReturnToSection_RestoresItsFilter` |
| NAV-RTE-05 | 122 | Old project links open their new sections | Low | Admin | B | `Routing_OldProjectLinks_OpenTheirSections` |

### Detail Panel (`NavigationDetailPanelTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| NAV-DET-01 | 142 | The detail pins, resizes by keyboard and keeps its mode for every detail | Medium | Editor | B,D | `DetailPanel_PinAndResize_KeptForEveryDetail` |
| NAV-DET-02 | 143 | The detail goes full screen and stays so after a reload | Medium | Editor | B,D | `DetailPanel_FullScreen_SurvivesReload` |
| NAV-DET-03 | 144 | On a phone the detail covers the list until Back to the list | Medium | Editor | B,Ph | `DetailPanel_Phone_CoversListUntilBack` |

## Annotations

### Bulk Create (`AnnotationBulkCreateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ANN-BLK-01 | 2 | Bulk create a responsibility and find it in the grid | Elementary | Editor | B | `Annotations_CreateResponsibility_AppearsInGrid` |
| ANN-BLK-02 | 18 | Compact lines create their missing ancestors | High | Editor | B | `BulkCreate_CompactLines_CreatesMissingAncestors` |
| ANN-BLK-03 | 64 | Review marks existing, duplicate and invalid lines and enforces the limit | High | Editor | B | `BulkCreate_ReviewProblems_BlockUntilRemoved` |
| ANN-BLK-04 | 67 | Bulk create over a deleted key offers Restore | Medium | Editor | B | `BulkCreate_DeletedKey_OffersRestore` |

### New Annotation (`AnnotationCreateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ANN-NEW-01 | 69 | New annotation creates an execution with its missing ancestors | High | Editor | B | `NewAnnotation_Execution_CreatesMissingAncestors` |
| ANN-NEW-02 | 71 | New annotation rejects dotted names and existing keys | Medium | Editor | B | `NewAnnotation_InvalidOrExistingKey_DisablesCreate` |
| ANN-NEW-04 | 73 | New annotation over a deleted key: Cancel, Restore and update, Create new | Medium | Editor | B | `NewAnnotation_DeletedKey_RestoresOrRecreates` |

### Filtering (`AnnotationFilterTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ANN-FLT-01 | 110 | Filter qualifiers, negation, OR, chips and count | High | Editor | B | `AnnotationFilter_Qualifiers_NarrowTheCatalog` |
| ANN-FLT-02 | 111 | Invalid filter terms warn and are ignored; the syntax examples fill the box | Low | Any | B | `AnnotationFilter_InvalidTerms_WarnAndAreIgnored` |
| ANN-FLT-03 | 112 | Filters bottom sheet on a phone | Medium | Any | B,Ph | `AnnotationFilter_PhoneSheet_BuildsTheQuery` |

### Catalog (`AnnotationCatalogTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ANN-CAT-01 | 113 | Tree shows every path by subject and by responsibility | High | Editor | B,D | `AnnotationTree_Perspectives_ShowEveryPath` |
| ANN-CAT-02 | 114 | Bulk bar disables, enables, adds and removes a label, and deletes selected rows with Undo | High | Editor | B,D | `AnnotationBulkBar_SelectedRows_ChangeDeleteAndUndo` |
| ANN-CAT-03 | 145 | An empty view shows the empty state with create actions | Medium | Editor | B | `AnnotationCatalog_EmptyView_OffersCreateActions` |
| ANN-CAT-04 | 146 | Reload the catalog shows an annotation created elsewhere | Low | Editor | B | `AnnotationCatalog_Reload_ShowsAnnotationsCreatedElsewhere` |
| ANN-CAT-05 | 148 | Returning from another section restores the filter, detail, tab, selection and sorting | Medium | Editor | B | `AnnotationCatalog_ReturnFromSection_RestoresPageMemory` |

### Detail (`AnnotationDetailTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ANN-DET-01 | 152 | Open an annotation from the list and close it back to the filtered list | High | Editor | B | `AnnotationDetail_OpenFromList_ClosesBackToTheFilter` |
| ANN-DET-02 | 154 | Keyboard: J and K move, Enter opens, Escape closes, / focuses the filter | Medium | Editor | B,D | `AnnotationDetail_Keyboard_MovesOpensAndCloses` |
| ANN-DET-03 | 176 | More actions copy the link and the key, and the header links lead to the graph and the configuration | Medium | Editor | B | `AnnotationDetail_MoreActions_CopyAndShowInGraph` |
| ANN-DET-04 | 177 | Breadcrumb, Lineage and Relations lead through ancestors and descendants | Medium | Editor | B | `AnnotationDetail_Lineage_LinksAncestorsAndDescendants` |
| ANN-DET-05 | 178 | History lists the creation, the update and the ancestor cause | Medium | Editor | B | `AnnotationHistory_CreateAndUpdate_ListsEntries` |
| ANN-DET-06 | 180 | Old annotation tab URLs open the configuration tabs | Low | Any | B | `AnnotationDetail_OldTabUrls_Redirect` |

### Annotation Form (`AnnotationFormTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ANN-FRM-01 | 181 | Title and Markdown description save, and the preview is safe | High | Editor | B | `AnnotationForm_TitleAndMarkdown_SaveAndPreviewSafely` |
| ANN-FRM-02 | 198 | Field limits show their messages and block Save | Medium | Editor | B | `AnnotationForm_FieldLimits_BlockSave` |
| ANN-FRM-03 | 200 | Disabled switch and validity dates drive the status | Medium | Editor | B | `AnnotationForm_DisabledAndDates_DriveStatus` |
| ANN-FRM-04 | 202 | JSON and YAML modes edit the annotation and guard the key fields | Medium | Editor | B | `AnnotationForm_JsonAndYaml_EditAndGuardKeyFields` |
| ANN-FRM-05 | 205 | Labels: create one, reuse it on a second annotation and filter by it | Medium | Editor | B | `AnnotationForm_Labels_CreateReuseAndFilter` |
| ANN-FRM-06 | 207 | Values editor: typed rows, duplicate keys and Edit all as JSON | Medium | Editor | B | `AnnotationForm_Values_TypedRowsAndDuplicates` |
| ANN-FRM-07 | 218 | Unit fields: unit type, definition and the Cron hint | Medium | Editor | B | `AnnotationForm_UnitFields_TypeDefinitionAndCronHint` |
| ANN-FRM-08 | 219 | Unsaved changes: Revert, Keep editing and Discard | High | Editor | B | `AnnotationForm_UnsavedChanges_RevertKeepOrDiscard` |

### Delete and Restore (`AnnotationDeleteTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ANN-DEL-01 | 220 | Delete from the detail with descendants, then Undo | High | Editor | B | `AnnotationDelete_WithDescendants_UndoRestores` |
| ANN-DEL-02 | 222 | A deleted key's detail shows the banner and restores with or without descendants | Medium | Editor | B | `AnnotationDelete_DeletedDetail_RestoresFromBanner` |

### Role Access (`AnnotationRoleTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| ANN-ROL-01 | 223 | A Reader gets no create actions, not even in an empty view | High | Reader | B | `AnnotationAccess_Reader_HasNoCreateActions` |
| ANN-ROL-02 | 225 | Exactly a Contributor can create, edit and delete | Medium | Contributor | B | `AnnotationAccess_Contributor_CreatesEditsDeletes` |
| ANN-ROL-03 | 226 | A Reader sees an existing annotation read-only, without a bulk bar | Medium | Reader | M | `AnnotationAccess_Reader_SeesDetailReadOnly` |

## Configurations

### Create and Delete (`ConfigurationCreateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| CFG-CRE-01 | 3 | Create empty, save a JSON document, and the Effective tab shows it | Elementary | Editor | B | `Configurations_SetDocument_EffectiveShowsIt` |
| CFG-CRE-02 | 63 | New configuration offers only annotations without their own configuration | High | Editor | B | `Configurations_NewDialog_OffersOnlyKeysWithoutConfiguration` |
| CFG-CRE-03 | 74 | Start from template copies the type template, and Create empty still inherits it | High | Owner | B,P | `Configurations_StartFromTemplate_CopiesTypeTemplate` |
| CFG-CRE-04 | 81 | Delete own needs the typed key and returns to the empty state | High | Editor | B | `Configurations_DeleteOwn_NeedsTypedKey` |

### Edit and Versions (`ConfigurationEditTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| CFG-EDT-01 | 83 | Unsaved changes ask before leaving, and Revert drops them | High | Editor | B | `Configurations_UnsavedChanges_AskBeforeLeaving` |
| CFG-EDT-02 | 95 | A syntax error disables Save and blocks the format switch | Medium | Editor | B | `Configurations_SyntaxError_BlocksSaveAndFormatSwitch` |
| CFG-EDT-03 | 109 | YAML mode edits and saves the document, and the format stays in the URL | Medium | Editor | B | `Configurations_YamlMode_SavesAndKeepsFormat` |
| CFG-EDT-04 | 123 | Versions lists every save and compares two versions | High | Editor | B | `Configurations_Versions_CompareShowsDiff` |

### Save Conflicts (`ConfigurationConflictTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| CFG-CON-01 | 125 | A concurrent change of the same value opens the conflict dialog, and Overwrite saves mine | High | Editor | B | `Configurations_ConcurrentChange_OverwriteSavesMine` |
| CFG-CON-02 | 140 | Reload, discard mine takes the stored version, and Copy mine copies mine | Medium | Editor | B | `Configurations_ConcurrentChange_ReloadTakesTheirs` |
| CFG-CON-03 | 150 | A concurrent change of another value merges without a conflict | Low | Editor | B | `Configurations_ConcurrentChangeElsewhere_MergesWithoutConflict` |

### Schema and Form (`ConfigurationSchemaFormTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| CFG-SCH-01 | 159 | The form follows the annotation's schema, and the Schema tab lists it | High | Editor | B | `Configurations_SchemaForm_SavesValue` |
| CFG-SCH-02 | 169 | A schema violation lists the errors, and Save anyway stores the document | High | Editor | B | `Configurations_SchemaViolation_SaveAnywayStoresIt` |
| CFG-SCH-03 | 174 | An inherited value shows as a placeholder, with Override and Reset to inherited | Medium | Editor | B | `Configurations_InheritedValue_OverrideAndReset` |
| CFG-SCH-04 | 185 | Lists and maps of the form add nested values | Medium | Editor | B | `Configurations_FormListsAndMaps_SaveNestedValues` |

### Inheritance (`ConfigurationInheritanceTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| CFG-INH-01 | 187 | A child's Effective merges the parent's configuration and keeps it after Delete own | High | Editor | B | `Configurations_Effective_MergesParentConfiguration` |
| CFG-INH-02 | 192 | Composition shows the step that set each line and links the parent | Medium | Editor | B | `Configurations_Composition_ShowsStepOfEachLine` |
| CFG-INH-03 | 210 | The breadcrumb and Lineage open the parent's configuration, and Annotation opens the annotation | Medium | Editor | B | `Configurations_BreadcrumbAndLineage_OpenParent` |

### Bindings and Resolved (`ConfigurationBindingTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| CFG-BND-01 | 214 | Resolved is hidden until Reveal and shows the evaluated bindings | High | Editor | B | `Configurations_Resolved_RevealShowsEvaluatedBindings` |
| CFG-BND-02 | 221 | A binding that cannot be evaluated shows the binding error | Medium | Editor | B | `Configurations_Resolved_ShowsBindingError` |
| CFG-BND-03 | 224 | The JSON editor completes @config paths, explains them on hover, and Bindings filters the reference | Medium | Editor | B | `Configurations_JsonEditor_CompletesAndExplainsBindings` |
| CFG-BND-04 | 227 | fx turns a form field into a binding, Use fills it, and Convert to value turns it back | Medium | Editor | B | `Configurations_FormBinding_UseAndConvertToValue` |

### List and Navigation (`ConfigurationNavigationTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| CFG-NAV-01 | 228 | A saved configuration is listed with its version and author, and its row opens the detail | High | Editor | B | `Configurations_List_ShowsVersionAndOpensDetail` |
| CFG-NAV-02 | 229 | The tabs keep the filter, and Close returns to the list | Low | Editor | B | `Configurations_Tabs_KeepFilterAndCloseToList` |
| CFG-NAV-03 | 230 | Returning to Configurations reopens the last configuration and tab | Medium | Editor | B,D | `Configurations_Section_ReopensLastConfiguration` |

### Roles (`ConfigurationRoleTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| CFG-ROL-01 | 231 | A Contributor creates and saves a configuration, and Resolved leaves secret values out | Medium | Contributor | B | `Configurations_Contributor_EditsWithoutSecrets` |
| CFG-ROL-02 | 232 | A Reader gets no New and no create actions | Medium | Reader | B | `Configurations_Reader_HasNoCreateActions` |
| CFG-ROL-03 | 233 | A Reader sees an existing configuration read-only | Medium | Reader | M | `Configurations_Reader_SeesConfigurationReadOnly` |

## Modeling

### Tabs and Access (`ModelingTabsTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| MOD-TAB-01 | 14 | Modeling opens on Schemas and every tab opens its own page | High | Admin | B | `Modeling_OpenTabs_EachTabOpensItsPage` |
| MOD-TAB-02 | 40 | Old URLs of the merged pages redirect to their Modeling tabs | Low | Editor | B | `Modeling_OldUrls_RedirectToModelingTabs` |
| MOD-TAB-03 | 42 | A Reader gets the Contributors only lock on every Modeling URL | High | Reader | B | `Modeling_Reader_GetsContributorsOnlyLock` |
| MOD-TAB-04 | 43 | A Contributor gets four tabs, the Administrators only lock and no Delete permanently | High | Contributor | B | `Modeling_Contributor_GetsNoAdministratorFeatures` |
| MOD-TAB-05 | 44 | Modeling returns to the last tab, detail and query after another section | Medium | Editor | B | `Modeling_ReturnFromOtherSection_RestoresLastTab` |
| MOD-TAB-06 | 46 | A dirty schema editor asks Discard changes? before another tab opens | Medium | Editor | B,D | `Modeling_LeaveDirtySchemaEditor_AsksToDiscard` |

### Schemas (`ModelingSchemaTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| MOD-SCH-01 | 92 | Create an annotation schema for a new annotation | Elementary | Editor | B | `Schemas_CreateAnnotationSchema_ListsVersionOne` |
| MOD-SCH-02 | 93 | New schema offers the kinds and only the types that can descend, then opens the editor | High | Editor | B | `Schemas_NewSchemaDialog_OpensDescendantTypeEditor` |
| MOD-SCH-03 | 96 | Create and delete a type schema | High | Owner | B,P | `Schemas_CreateAndDeleteTypeSchema_UpdatesTypeRow` |
| MOD-SCH-04 | 99 | Meta-schema and syntax errors block saving a schema, and Revert restores it | High | Editor | B | `Schemas_InvalidDocument_BlocksSaveUntilFixed` |
| MOD-SCH-05 | 101 | A schema that breaks a configuration lists it, and Save anyway forces it | High | Editor | B | `Schemas_BreakingChange_SaveAnywayForcesIt` |
| MOD-SCH-06 | 103 | The form preview renders the schema and keeps sample data | Medium | Editor | B | `Schemas_FormPreview_RendersObjectSchema` |
| MOD-SCH-07 | 105 | Schema versions show the history and a comparison, and a typed delete ends them | Medium | Editor | B | `Schemas_VersionsAndDelete_ShowHistoryThenNone` |

### Templates (`ModelingTemplateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| MOD-TPL-01 | 147 | Create, edit, version and delete a template | High | Owner | B,P | `Templates_CreateEditAndDelete_KeepsVersions` |
| MOD-TPL-02 | 149 | A concurrent template change opens the conflict dialog | Medium | Owner | B,P | `Templates_ConcurrentChange_OpensConflictDialog` |

### Export and Import (`ModelingExportImportTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| MOD-EXI-01 | 151 | Export the view as a folder of JSON files and check it back in | High | Admin | B | `Export_FolderOfJsonFiles_ChecksBackInUnchanged` |
| MOD-EXI-02 | 153 | Export only some kinds, require one kind, and export a Git repository | Medium | Admin | B | `Export_KindsAndGitFormat_ShapeTheArchive` |
| MOD-EXI-03 | 155 | Check an archive with a new annotation, then Import now | High | Admin | B | `Import_CheckThenImportNow_CreatesAnnotation` |
| MOD-EXI-04 | 156 | Skip keeps an existing document, and the result shows again after a reload | Medium | Admin | B | `Import_SkipExisting_KeepsItAfterReload` |
| MOD-EXI-05 | 171 | A file that is not a zip is refused, and excluded, ignored and invalid files are reported | Medium | Admin | B | `Import_FaultyArchive_ReportsEveryProblem` |
| MOD-EXI-06 | 172 | A schema that breaks a configuration fails the import unless forced | Medium | Admin | B | `Import_BreakingSchema_FailsUnlessForced` |
| MOD-EXI-07 | 173 | The toaster reports an import that ended after leaving the page | Low | Admin | B | `Import_EndedElsewhere_ToastShowsResult` |
| MOD-EXI-08 | 175 | Overwrite, the default, updates an existing document | High | Admin | B | `Import_OverwriteExisting_UpdatesDocument` |

### Deleted (`ModelingDeletedTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| MOD-DEL-01 | 196 | Deleted lists the root of a deletion and Show cascaded adds its descendants | High | Editor | B | `Deleted_ShowCascaded_ListsDescendants` |
| MOD-DEL-02 | 199 | Restore a deletion with and without its descendants | High | Editor | B | `Deleted_Restore_WithAndWithoutDescendants` |
| MOD-DEL-03 | 201 | Bulk restore of selected deletions | Low | Editor | B | `Deleted_BulkRestore_RestoresSelection` |
| MOD-DEL-04 | 203 | Delete permanently needs the typed key, or the typed count for several | Medium | Admin | B | `Deleted_DeletePermanently_NeedsTypedConfirmation` |

### Compare Views (`ModelingCompareTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| MOD-CMP-01 | 208 | Compare views is disabled without another view, then preselects it and shows the difference | Medium | Owner | B,P | `CompareViews_OtherView_PreselectedAndDiffed` |

## Graph

### Overview (`GraphOverviewTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| GRA-OVR-01 | 16 | Graph focused on a created tree shows its neighbourhood and opens a node by keyboard | Elementary | Editor | B | `Graph_FocusOnCreatedTree_OpensNodeByKeyboard` |
| GRA-OVR-02 | 56 | Arrow keys move along the links, Enter opens the annotation and Escape closes it | Medium | Editor | B | `Graph_ArrowKeys_MoveAlongLinksAndOpenNode` |
| GRA-OVR-03 | 57 | On a phone the legend is a Types popover, the toolbar is icon-only and a node's detail covers the graph | Medium | Editor | B,Ph | `Graph_PhoneLayout_CollapsesLegendAndCoversGraph` |

### Legend (`GraphLegendTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| GRA-LEG-01 | 58 | Hiding types removes their nodes, survives a catalog reload, and the legend brings them back | High | Editor | B | `GraphLegend_HideTypes_RemovesNodesUntilShownAgain` |
| GRA-LEG-02 | 59 | Enter on a focused legend chip toggles its type while a node is open | Low | Editor | B,D | `GraphLegend_EnterOnChip_TogglesTypeWithNodeOpen` |

### Focus and Filter (`GraphFocusTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| GRA-FOC-01 | 60 | Focus starts from the last opened annotation, the depth changes the neighbourhood and Show everything leaves it | High | Editor | B | `GraphFocus_FocusAndDepth_ChangeNeighbourhood` |
| GRA-FOC-02 | 62 | The filter adds its matches to the focus and keeps every node of the whole view | Medium | Editor | B | `GraphFocus_Filter_AddsMatchesAndKeepsWholeView` |

### Toolbar (`GraphToolbarTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| GRA-TBR-01 | 91 | Layout, Fit and Reheat redraw the graph without changing its annotations, and Export PNG downloads it | Medium | Editor | B | `GraphToolbar_LayoutAndExport_KeepNodesAndDownloadPng` |
| GRA-TBR-02 | 94 | Open as list shows the drawn annotations in the grid | High | Editor | B | `GraphToolbar_OpenAsList_ShowsDrawnAnnotationsInGrid` |

### Node Detail (`GraphNodeDetailTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| GRA-DET-01 | 97 | A node's detail opens beside the graph, and its tabs, breadcrumb and close keep the graph's query | High | Editor | B | `GraphDetail_OpenByUrl_KeepsGraphQueryOnTabsAndClose` |
| GRA-DET-02 | 98 | Lineage, Show in list and Show in graph move between the graph and the list | Medium | Editor | B | `GraphDetail_LineageAndShowInList_SwitchListAndGraph` |
| GRA-DET-03 | 100 | Deleting a node from its detail removes it from the graph, and Undo brings it back | Medium | Editor | B | `GraphDetail_DeleteAndUndo_RemovesAndRestoresNode` |
| GRA-DET-04 | 102 | An unknown node shows Annotation not found, and the old tab links of a node redirect to its configuration with their query | Low | Any | B | `GraphDetail_UnknownKeyAndOldTabs_ShowNotFoundAndRedirect` |

### Create (`GraphCreateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| GRA-CRE-01 | 128 | New in the Graph top bar creates an annotation and opens it in the graph | High | Editor | B | `GraphCreate_NewInTopBar_OpensAnnotationInGraph` |
| GRA-CRE-02 | 130 | The empty graph of a new project offers New annotation and Bulk create, and Bulk create draws the tree | High | Owner | B,P | `GraphCreate_BulkCreateInEmptyProject_DrawsTree` |
| GRA-CRE-03 | 133 | A Reader has no create actions on Graph | Medium | Reader | B | `GraphCreate_Reader_HasNoCreateActions` |
| GRA-CRE-04 | 136 | A create dialog left open on Graph does not reopen on Annotations after Back | Low | Editor | B | `GraphCreate_DialogLeftOpen_DoesNotReopenOnAnnotations` |

### Remembered State (`GraphMemoryTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| GRA-MEM-01 | 138 | Coming back to Graph through the navigation restores its URL state and hidden types until a reload | Medium | Editor | B | `GraphMemory_SidebarReturn_RestoresStateUntilReload` |

## Key-Values

### Create (`KeyValueCreateTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| KV-CRE-01 | 4 | Create a public key value and read it back | Elementary | Editor | B | `KeyValues_CreateAndRead` |
| KV-CRE-02 | 15 | New key validates the key grammar and the value size | High | Editor | B | `KeyValues_NewKeyInvalidInput_ShowsFieldErrors` |
| KV-CRE-03 | 45 | New key with the name of an existing key shows a field error | Medium | Editor | B | `KeyValues_NewKeyExistingName_ShowsKeyError` |
| KV-CRE-04 | 47 | A secret reference is validated and shows the Reference badge | Medium | Secrets | B | `KeyValues_NewSecretReference_ShowsReferenceBadge` |

### Detail (`KeyValueDetailTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| KV-DET-01 | 48 | Edit a public value in place and save it | High | Editor | B | `KeyValues_EditPublicValue_SavesNewVersions` |
| KV-DET-02 | 49 | The metadata and the binding source of a key | Medium | Editor | B | `KeyValues_BindingSource_ChangesCopiedBinding` |
| KV-DET-03 | 51 | Unsaved changes ask before switching to Versions | Medium | Editor | B | `KeyValues_UnsavedChanges_AskBeforeVersions` |
| KV-DET-04 | 53 | A concurrent change offers Reload or Overwrite | High | Editor | B | `KeyValues_ConcurrentChange_OffersReloadOrOverwrite` |

### Secrets (`KeyValueSecretTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| KV-SEC-01 | 84 | Create a Standard secret, reveal, copy and hide it | High | Secrets | B | `KeyValues_StandardSecret_RevealsCopiesAndHides` |
| KV-SEC-02 | 85 | A revealed secret is masked again after 60 seconds | Low | Secrets | B | `KeyValues_RevealedSecret_MasksAfterSixtySeconds` |
| KV-SEC-03 | 86 | Set a new value for a secret, then make it public | High | Secrets | B | `KeyValues_SetNewSecretValue_SavesAndMakesPublic` |
| KV-SEC-04 | 87 | Change the protection of a secret to HSM | Medium | Owner | B,P | `KeyValues_ChangeProtectionToHsm_KeepsValue` |
| KV-SEC-05 | 89 | A HYOK value is encrypted in the browser for the recipients | Medium | Owner | B,P | `KeyValues_HyokValue_EncryptsInBrowser` |
| KV-SEC-06 | 90 | Bulk Change protection keeps the keys it could not change selected | Medium | Owner | B,D,P | `KeyValues_BulkChangeProtection_KeepsFailedSelected` |

### Versions (`KeyValueVersionTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| KV-VER-01 | 126 | A JSON value edited in the code editor: versions load on demand, View and Compare | Medium | Editor | B | `KeyValues_JsonVersions_LoadOnDemandAndCompare` |
| KV-VER-02 | 127 | Reveal and compare secret versions | Medium | Secrets | B | `KeyValues_SecretVersions_RevealAndCompare` |

### List (`KeyValueListTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| KV-LST-01 | 129 | Search, level filter and the query kept by the detail | Medium | Owner | B,P | `KeyValues_SearchAndLevelFilter_NarrowTheList` |
| KV-LST-02 | 131 | The row menu copies the key and the binding and opens the detail | Medium | Owner | B,D,P | `KeyValues_RowMenu_CopiesAndOpens` |
| KV-LST-03 | 134 | Switching the project drops the key, the selection and the secrets opt-in | Low | Owner | B,D,P | `KeyValues_SwitchProject_DropsKeySelectionAndOptIn` |

### Lifecycle (`KeyValueLifecycleTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| KV-LIF-01 | 164 | Delete a key from the row menu and restore it from the Deleted keys | High | Owner | B,D,P | `KeyValues_DeleteFromRowMenu_RestoresFromDetail` |
| KV-LIF-02 | 165 | Remove keys for good with Delete permanently and Purge | Medium | Owner | B,D,P | `KeyValues_DeletePermanentlyAndPurge_RemoveForGood` |
| KV-LIF-03 | 167 | Bulk delete and bulk restore of selected keys | Medium | Owner | B,D,P | `KeyValues_BulkDeleteAndRestore_ReturnsKeys` |
| KV-LIF-04 | 168 | New key over a deleted key continues its versions | Low | Owner | B,D,P | `KeyValues_NewKeyOverDeleted_ContinuesVersions` |

### Import and Export (`KeyValueImportExportTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| KV-IEX-01 | 189 | Import .env lines and mark the password key as a secret | High | Secrets | B | `KeyValues_ImportDotEnv_MarksPasswordAsSecret` |
| KV-IEX-02 | 190 | Import a key/value document from a file, then a flat JSON plan of create, update and unchanged | Medium | Secrets | B | `KeyValues_ImportDocumentThenFlat_PlansEachAction` |
| KV-IEX-03 | 191 | Import refuses unreadable text and invalid keys | Medium | Editor | B | `KeyValues_ImportUnreadableText_StaysOnSource` |
| KV-IEX-04 | 193 | Replace under a prefix deletes the unlisted keys; Undo deletes restores them | Medium | Owner | B,P | `KeyValues_ImportReplaceUnderPrefix_UndoRestores` |
| KV-IEX-05 | 194 | Export .env, flat JSON and the key/value document without secret values | High | Secrets | B | `KeyValues_ExportWithoutSecrets_LeavesSecretsOut` |
| KV-IEX-06 | 195 | Export with secret values on request | Medium | Owner | B,P | `KeyValues_ExportWithSecrets_IncludesValues` |

### Roles (`KeyValueRoleTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| KV-ROL-01 | 212 | A Reader sees Values store read-only and can export it | Medium | Reader | B | `KeyValues_Reader_SeesReadOnlyStore` |
| KV-ROL-02 | 213 | A Contributor creates and edits only public keys | Medium | Contributor | B | `KeyValues_Contributor_CreatesPublicKeysOnly` |

## Settings

### Project and Roles (`SettingsProjectTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| SET-PRJ-01 | 17 | Settings page shows every section of the project for an Administrator | Elementary | Admin | B | `Settings_OpenAsAdministrator_ShowsEverySection` |
| SET-PRJ-02 | 39 | Administrator changes the project description and it shows on the organization overview | High | Owner | B,P | `Settings_SaveDescription_ShowsOnOrganizationOverview` |
| SET-PRJ-03 | 41 | A Reader sees the project settings read-only | High | Reader | B | `Settings_OpenAsReader_ShowsEverythingReadOnly` |
| SET-PRJ-04 | 61 | A Contributor changes the browser settings but not the project-wide ones | Medium | Contributor | B | `Settings_OpenAsContributor_ChangesOnlyBrowserSettings` |

### Browser Settings (`SettingsBrowserTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| SET-BRW-01 | 65 | The default time zone is kept in this browser, and new annotations start and are saved in it | High | Editor | B | `Settings_DefaultTimeZone_NewAnnotationStartsInIt` |
| SET-BRW-02 | 78 | The project's date format changes how dates are shown | High | Editor | B | `Settings_DateFormat_ChangesShownDates` |
| SET-BRW-03 | 88 | The project renames, recolors and re-icons an annotation type | High | Editor | B | `Settings_CustomizeType_ShowsOnTemplates` |
| SET-BRW-04 | 108 | A type's plural needs a name, and one type or all types can be reset | Medium | Editor | B | `Settings_ResetTypes_RestoresBuiltInNames` |

### Preferences (`SettingsPreferencesTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| SET-PRF-01 | 118 | Preferences are kept in this browser and 'Reset preferences' restores the defaults | Medium | Any | B | `Preferences_ChangeAndReset_RestoresDefaults` |
| SET-PRF-02 | 124 | A personal date format in Preferences wins over the project's | Medium | Editor | B | `Preferences_PersonalDateFormat_WinsOverProject` |
| SET-PRF-03 | 137 | Personal type names in Preferences win over the project's on Templates and Annotations | Medium | Editor | B | `Preferences_PersonalTypeName_WinsOverProject` |

### Machine Authentication (`SettingsMachineAuthenticationTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| SET-MAC-01 | 157 | Administrator changes the credential kinds, the default kind and the certificate lifetime | High | Owner | B,P | `MachinePolicy_ChangeKindsAndLifetime_IsKept` |
| SET-MAC-02 | 158 | Anyone downloads the CA certificate as PEM | Medium | Any | B | `CertificateAuthority_Download_SavesPem` |

### Secrets Policy (`SettingsSecretsPolicyTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| SET-SEC-01 | 166 | Turning off the mTLS rule asks first; Cancel keeps it and the confirmed change is kept | High | Owner | B,P | `SecretsPolicy_TurnOffMtls_AsksFirstAndIsKept` |
| SET-SEC-02 | 170 | Disabling a level that keys still use shows the counts per view | High | Owner | B,P | `SecretsPolicy_DisableLevelInUse_ShowsCountsPerView` |
| SET-SEC-03 | 179 | A customer key can be saved, but BYOK cannot be allowed before it is verified | Medium | Owner | B,P | `SecretsPolicy_AllowByokUnverified_IsRefused` |
| SET-SEC-04 | 186 | HYOK is allowed once a recipient exists, becomes the default level and protects its last recipient | High | Owner | B,P | `SecretsPolicy_AllowHyok_ProtectsLastRecipient` |
| SET-SEC-05 | 188 | Saving a secrets policy that another writer changed meanwhile is refused | Low | Owner | B,P | `SecretsPolicy_SaveStalePolicy_IsRefused` |

### Keys and Recipients (`SettingsKeysRecipientsTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| SET-KEY-01 | 197 | Administrator rotates the Standard key after confirming | Medium | Owner | B,P | `SecretKeys_RotateStandardKey_ShowsNewVersion` |
| SET-KEY-02 | 211 | Administrator adds, disables, enables and removes a recipient, and invalid keys are refused | High | Owner | B,P | `Recipients_AddDisableRemove_RefusesInvalidKeys` |
| SET-KEY-03 | 215 | Allowing HSM creates the HSM key, which an Administrator rotates | Medium | Owner | B,P | `SecretKeys_AllowHsm_CreatesAndRotatesHsmKey` |

## Monitoring

### Monitor Page (`MonitorTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| MON-PAG-01 | 7 | Monitor opens on Usage and switches to Audit through the tabs | Elementary | Admin | B | `Monitor_SwitchTabs_ShowsUsageAndAudit` |
| MON-PAG-02 | 9 | Monitor is for Administrators only | High | Reader | B | `Monitor_OpenAsReader_ShowsAdministratorsOnly` |
| MON-PAG-03 | 10 | The usage report follows the chosen date range and refuses a reversed one | Medium | Admin | B | `MonitorUsage_ChangeRange_ShowsReportOfRange` |
| MON-PAG-04 | 11 | Old Usage and Audit links open the Monitor tabs | Medium | Admin | B | `Monitor_OpenOldLinks_OpensTheirTabs` |

### Audit Trail (`MonitorAuditTests`)

| Reference | Testiny | Title | Priority | Role | Mode | Test |
|---|---|---|---|---|---|---|
| MON-AUD-01 | 25 | Creating and revealing a secret is recorded in the audit trail | High | Admin | B | `MonitorAudit_CreateAndRevealSecret_RecordsBothEvents` |
| MON-AUD-02 | 29 | Audit filters narrow the events by operation, view and dates | Medium | Admin | B | `MonitorAudit_ApplyFilters_NarrowsEvents` |
| MON-AUD-03 | 32 | Project-wide secret changes and an export with secret values are recorded in the audit trail | High | Owner | B,P | `MonitorAudit_ProjectSecretChanges_AreRecorded` |
