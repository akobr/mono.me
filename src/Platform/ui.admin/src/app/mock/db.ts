import type { AccessPoint, Account, AccountRole, Annotation, View } from '@42for.net/2splatform.sdk'
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
  deleted: string[]
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
}

export type MockScenario = 'default' | 'new-user' | 'large'

const scenarios: MockScenario[] = ['default', 'new-user', 'large']
const storageKey = '2s-admin:mock-db'
const scenarioKey = '2s-admin:mock-scenario'

export const mockUser: MockUser = { id: 'user_01MOCKADA', name: 'Ada Lovelace', email: 'ada@example.com' }

const now = () => new Date().toISOString()

function accessPoint(key: string, members: Record<string, AccountRole>): AccessPoint {
  return {
    Key: key,
    AccessMap: members,
    MachineAuthentication: { CredentialKind: 'ApiKey', CertificateLifetimeDays: 90 },
  }
}

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
  if (scenario === 'new-user') {
    return { scenario, user: mockUser, account: null, accessPoints: {}, views: {}, annotations: {} }
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
      'acme.billing': accessPoint('acme.billing', {
        [id]: 'Owner',
        user_01MOCKGRACE: 'Contributor',
        user_01MOCKALAN: 'Reader',
      }),
      'acme.shipping': accessPoint('acme.shipping', { [id]: 'Contributor', user_01MOCKGRACE: 'Owner' }),
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
          CreatedAt: now(),
          CreatedBy: id,
        },
        {
          Name: 'staging',
          Description: 'Pre-release changes',
          IsDefault: false,
          IsRegistered: true,
          CreatedAt: now(),
          CreatedBy: id,
        },
      ],
    },
    annotations: {},
  }
}

export function currentScenario(): MockScenario {
  const fromUrl = new URLSearchParams(location.search).get('mock-scenario') as MockScenario | null

  if (fromUrl && scenarios.includes(fromUrl)) {
    sessionStorage.setItem(scenarioKey, fromUrl)
    sessionStorage.removeItem(storageKey)
  }

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

/** The annotations of a scope: the generated base with the stored overrides applied. */
export function annotationsOf(db: MockDb, org: string, project: string, view: string): Map<string, Annotation> {
  const id = `${org}/${project}/${view}`
  const seed = scopeSeed(db.scenario, org, project, view)
  const cacheKey = `${db.scenario}:${id}`

  if (seed && !generated.has(cacheKey)) {
    generated.set(cacheKey, generateAnnotations(seed))
  }

  const overrides = db.annotations[id]
  const result = new Map((generated.get(cacheKey) ?? []).map((annotation) => [annotation.AnnotationKey, annotation]))

  for (const key of overrides?.deleted ?? []) {
    result.delete(key)
  }
  for (const [key, annotation] of Object.entries(overrides?.upserts ?? {})) {
    result.set(key, annotation)
  }

  return result
}

export function overridesOf(db: MockDb, org: string, project: string, view: string): AnnotationOverrides {
  return (db.annotations[`${org}/${project}/${view}`] ??= { upserts: {}, deleted: [] })
}
