import type {
  AccessPoint,
  Account,
  AccountRole,
  Annotation,
  AnnotationAuditEntry,
  FederatedIssuer,
  Invitation,
  KeyValueAuditEvent,
  KeyValuePolicy,
  MachineAccess,
  MachineCredentialKind,
  SharedCertificate,
  View,
} from '@42for.net/2splatform.sdk'
import type { KeyValueScopeStore } from './key-values'
import { generateAnnotations, type ScopeSeed } from './annotations'

// In-memory data of the mock API (vite --mode e2e). It is kept in sessionStorage, so it survives the full-page
// navigations of the mock sign-in, and it is reset by closing the tab or by calling window.storytellerMock.reset().
// Annotations are generated from seeds per scope; only their changes (overrides) are stored.

export interface MockUser {
  id: string
  name: string
  email: string
}

export interface AnnotationOverrides {
  upserts: Record<string, Annotation>
  /** Keys missing from the catalog: deleted (soft, then in `trash`) or deleted permanently. */
  deleted: string[]
  /** Soft-deleted annotations, with their Deletion, until a restore, a re-create or a permanent delete. */
  trash?: Record<string, Annotation>
  /** The change history per key, oldest first; a key without one gets a synthetic Created entry. */
  audit?: Record<string, AnnotationAuditEntry[]>
}

export interface MockDb {
  scenario: MockScenario
  user: MockUser
  /** null: the user has no Storyteller account yet (onboarding). */
  account: Account | null
  accessPoints: Record<string, AccessPoint>
  /** Registered views per project key ("org.project"). */
  views: Record<string, View[]>
  /** Changed annotations per scope ("org/project/view"). */
  annotations: Record<string, AnnotationOverrides>
  /** Changed versioned documents (configurations, templates, schemas) per scope; null marks a deletion. */
  documents?: Record<string, Record<string, { versions: unknown[] } | null>>
  invitations?: Invitation[]
  /** Machines per project key. */
  machines?: Record<string, MachineAccess[]>
  /** Shared certificates per project key. */
  certificates?: Record<string, SharedCertificate[]>
  /** Trusted issuers of Federated machines per project key (424-46). */
  issuers?: Record<string, FederatedIssuer[]>
  /** Key/value pairs per scope ("org/project/view"), with their versions and the deleted state. */
  keyValues?: Record<string, KeyValueScopeStore>
  /** Key/value policies per project key. */
  keyValuePolicies?: Record<string, KeyValuePolicy>
  /** The secret audit trail per project key, newest last. */
  keyValueAudit?: Record<string, KeyValueAuditEvent[]>
}

export type MockScenario = 'default' | 'new-user' | 'invited' | 'large'

const scenarios: MockScenario[] = ['default', 'new-user', 'invited', 'large']
const storageKey = '2s-admin:mock-db'
const scenarioKey = '2s-admin:mock-scenario'

export const mockUser: MockUser = { id: 'user_01MOCKADA', name: 'Ada Lovelace', email: 'ada@example.com' }

/** Accounts of the mock data, for member names. */
export const mockDirectory: Record<string, { name: string; email: string }> = {
  user_01MOCKADA: { name: 'Ada Lovelace', email: 'ada@example.com' },
  user_01MOCKGRACE: { name: 'Grace Hopper', email: 'grace@example.com' },
  user_01MOCKALAN: { name: 'Alan Turing', email: 'alan@example.com' },
}

const now = () => new Date().toISOString()

function accessPoint(
  key: string,
  members: Record<string, AccountRole>,
  kinds: MachineCredentialKind[] = ['ApiKey'],
): AccessPoint {
  return {
    Key: key,
    AccessMap: members,
    MachineAuthentication: {
      EnabledCredentialKinds: kinds,
      CredentialKind: 'ApiKey',
      CertificateLifetimeDays: 90,
    },
  }
}

/** The trusted issuer of the GitHub organization acme (id 65), as the docs set it up. */
const githubIssuer = (): FederatedIssuer => ({
  Name: 'github',
  Issuer: 'https://token.actions.githubusercontent.com',
  KeySource: 'Discovery',
  RequiredClaims: { repository_owner_id: ['65'] },
  Description: 'GitHub Actions of the acme organization',
})

/** The token endpoint of a project on the origin the mock API serves (the API base URL of the e2e build is /api). */
const mockTokenEndpoint = (org: string, project: string) =>
  `${typeof location === 'undefined' ? 'https://api.example.com' : location.origin}/api/v1/${org}/${project}/access/token`

/** Seeds of the generated annotation catalogs per scope. */
export function scopeSeed(scenario: MockScenario, org: string, project: string, view: string): ScopeSeed | undefined {
  const seeds: Record<string, number> = {
    'acme/billing/default': scenario === 'large' ? 500 : 10,
    'acme/billing/staging': 4,
    'acme/shipping/default': 6,
    'northwind/portal/default': 3,
  }
  const subjects = seeds[`${org}/${project}/${view}`]
  return subjects ? { project, view, subjects } : undefined
}

export function createSeed(scenario: MockScenario): MockDb {
  if (scenario === 'new-user' || scenario === 'invited') {
    const seed = createSeed('default')
    // The same organizations exist, but the user is a member of none of them.
    const accessPoints = Object.fromEntries(
      Object.entries(seed.accessPoints).map(([key, point]) => {
        const { [mockUser.id]: _removed, ...members } = point.AccessMap ?? {}
        return [key, { ...point, AccessMap: members }]
      }),
    )
    return {
      ...seed,
      scenario,
      account: null,
      accessPoints,
      invitations:
        scenario === 'invited'
          ? [
              {
                Id: 'inv_01MOCKBILLING',
                AccessPointKey: 'acme.billing',
                Email: mockUser.email,
                Role: 'Contributor',
                Status: 'Pending',
                InvitedById: 'user_01MOCKGRACE',
                InvitedByName: 'Grace Hopper',
                CreatedAt: now(),
                ExpiresAt: new Date(Date.now() + 7 * 86_400_000).toISOString(),
                IsEmailSent: true,
              },
            ]
          : [],
    }
  }

  const id = mockUser.id
  const accessMap: Record<string, AccountRole> = {
    acme: 'Owner',
    'acme.billing': 'Owner',
    'acme.shipping': 'Contributor',
    'northwind.portal': 'Reader',
  }

  return {
    scenario,
    user: mockUser,
    account: { Id: id, UserName: mockUser.email, Name: mockUser.name, AccessMap: accessMap },
    accessPoints: {
      acme: accessPoint('acme', { [id]: 'Owner', user_01MOCKGRACE: 'Administrator' }),
      // Both acme projects let machines federate with GitHub Actions (424-46); the user administers billing and
      // contributes to shipping.
      'acme.billing': accessPoint(
        'acme.billing',
        { [id]: 'Owner', user_01MOCKGRACE: 'Contributor', user_01MOCKALAN: 'Reader' },
        ['ApiKey', 'Federated'],
      ),
      'acme.shipping': accessPoint('acme.shipping', { [id]: 'Contributor', user_01MOCKGRACE: 'Owner' }, [
        'ApiKey',
        'Federated',
      ]),
      northwind: accessPoint('northwind', { user_01MOCKALAN: 'Owner' }),
      'northwind.portal': accessPoint('northwind.portal', { [id]: 'Reader', user_01MOCKALAN: 'Owner' }),
    },
    views: {
      'acme.billing': [
        {
          Name: 'default',
          Description: 'Production catalog',
          IsDefault: true,
          IsRegistered: true,
          Created: { At: now(), By: { Kind: 'Account', Id: id, Name: mockUser.name } },
        },
        {
          Name: 'staging',
          Description: 'Pre-release changes',
          IsDefault: false,
          IsRegistered: true,
          Created: { At: now(), By: { Kind: 'Account', Id: id, Name: mockUser.name } },
        },
      ],
    },
    annotations: {},
    invitations: [
      {
        Id: 'inv_01MOCKKATHERINE',
        AccessPointKey: 'acme.billing',
        Email: 'katherine@example.com',
        Role: 'Reader',
        Status: 'Pending',
        InvitedById: id,
        InvitedByName: mockUser.name,
        CreatedAt: now(),
        ExpiresAt: new Date(Date.now() + 5 * 86_400_000).toISOString(),
        IsEmailSent: true,
      },
    ],
    machines: {
      'acme.billing': [
        {
          Id: 'mach-invoicing-worker',
          ObjectId: 'obj_01',
          Scope: 'DefaultRead',
          CredentialKind: 'ApiKey',
          LastRenewalAt: new Date(Date.now() - 20 * 86_400_000).toISOString(),
        },
        {
          Id: 'mach-billing-sync',
          ObjectId: 'obj_02',
          Scope: 'ConfigurationReadWrite',
          AnnotationKey: 'exe.northwind.invoicing.prod',
          CredentialKind: 'Certificate',
          CertificateThumbprint: 'A1B2C3D4E5F60718293A4B5C6D7E8F9012345678',
          CertificateLifetimeDays: 90,
          CertificateNotAfter: new Date(Date.now() + 20 * 86_400_000).toISOString(),
          LastRenewalAt: new Date(Date.now() - 70 * 86_400_000).toISOString(),
        },
        {
          Id: 'mach-release-pipeline',
          ObjectId: 'mach-release-pipeline',
          AccessKey: '',
          Scope: 'AnnotationReadWrite',
          CredentialKind: 'Federated',
          FederationRules: [
            {
              Issuer: 'github',
              Audience: mockTokenEndpoint('acme', 'billing'),
              Subject: 'repo:acme/billing:environment:production',
              Claims: { repository_id: ['74'] },
            },
          ],
        },
      ],
    },
    issuers: {
      'acme.billing': [githubIssuer()],
      'acme.shipping': [githubIssuer()],
    },
    certificates: {
      'acme.billing': [
        {
          Thumbprint: '9F8E7D6C5B4A39281706F5E4D3C2B1A098765432',
          Label: 'ci-pipeline',
          NotBefore: new Date(Date.now() - 340 * 86_400_000).toISOString(),
          NotAfter: new Date(Date.now() + 25 * 86_400_000).toISOString(),
          IsRevoked: false,
        },
      ],
    },
  }
}

/**
 * Applies `?mock-scenario=` of the page load: selects the scenario and resets its data. It runs once at start-up,
 * not per request, so the parameter does not wipe edits while it stays in the URL, and a redirect (for example to
 * /login) before the first request does not lose it.
 */
export function applyScenarioFromUrl(): void {
  const fromUrl = new URLSearchParams(location.search).get('mock-scenario') as MockScenario | null

  if (fromUrl && scenarios.includes(fromUrl)) {
    sessionStorage.setItem(scenarioKey, fromUrl)
    sessionStorage.removeItem(storageKey)
  }
}

export function currentScenario(): MockScenario {
  return (sessionStorage.getItem(scenarioKey) as MockScenario | null) ?? 'default'
}

export function loadDb(): MockDb {
  const scenario = currentScenario()
  const stored = sessionStorage.getItem(storageKey)
  return stored
    ? ({ scenario, annotations: {}, ...(JSON.parse(stored) as Partial<MockDb>) } as MockDb)
    : createSeed(scenario)
}

export function saveDb(db: MockDb): void {
  sessionStorage.setItem(storageKey, JSON.stringify(db))
}

export function resetDb(scenario: MockScenario = currentScenario()): MockDb {
  sessionStorage.setItem(scenarioKey, scenario)
  const db = createSeed(scenario)
  saveDb(db)
  return db
}

const generated = new Map<string, Annotation[]>()
// The last result per scope: the large scenario asks for 16,000 annotations on every request.
const merged = new Map<string, { signature: string; result: ReadonlyMap<string, Annotation> }>()

/**
 * The annotations of a scope: the generated base with the stored overrides applied. The same map is returned
 * while nothing changed, so callers can cache derived data on it; it must not be mutated.
 */
export function annotationsOf(db: MockDb, org: string, project: string, view: string): ReadonlyMap<string, Annotation> {
  const id = `${org}/${project}/${view}`
  const seed = scopeSeed(db.scenario, org, project, view)
  const cacheKey = `${db.scenario}:${id}`

  if (seed && !generated.has(cacheKey)) {
    generated.set(cacheKey, generateAnnotations(seed))
  }

  const overrides = db.annotations[id]
  const signature = `${cacheKey}:${JSON.stringify(overrides ?? null)}`
  const cached = merged.get(id)
  if (cached?.signature === signature) {
    return cached.result
  }

  const result = new Map((generated.get(cacheKey) ?? []).map((annotation) => [annotation.AnnotationKey, annotation]))

  for (const key of overrides?.deleted ?? []) {
    result.delete(key)
  }
  for (const [key, annotation] of Object.entries(overrides?.upserts ?? {})) {
    result.set(key, annotation)
  }

  merged.set(id, { signature, result })
  return result
}

export function overridesOf(db: MockDb, org: string, project: string, view: string): AnnotationOverrides {
  return (db.annotations[`${org}/${project}/${view}`] ??= { upserts: {}, deleted: [] })
}
