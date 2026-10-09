import type { AccessPoint, Account, AccountRole, View } from '@42for.net/2splatform.sdk'

// In-memory data of the mock API (vite --mode e2e). It is kept in sessionStorage, so it survives the full-page
// navigations of the mock sign-in, and it is reset by closing the tab or by calling window.storytellerMock.reset().

export interface MockUser {
  id: string
  name: string
  email: string
}

export interface MockDb {
  user: MockUser
  /** null: the user has no Storyteller account yet (onboarding). */
  account: Account | null
  accessPoints: Record<string, AccessPoint>
  /** Registered views per project key ("org.project"). */
  views: Record<string, View[]>
}

export type MockScenario = 'default' | 'new-user'

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

export function createSeed(scenario: MockScenario): MockDb {
  if (scenario === 'new-user') {
    return { user: mockUser, account: null, accessPoints: {}, views: {} }
  }

  const id = mockUser.id
  const accessMap: Record<string, AccountRole> = {
    acme: 'Owner',
    'acme.billing': 'Owner',
    'acme.shipping': 'Contributor',
    'northwind.portal': 'Reader',
  }

  return {
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
  }
}

export function currentScenario(): MockScenario {
  const fromUrl = new URLSearchParams(location.search).get('mock-scenario')

  if (fromUrl === 'default' || fromUrl === 'new-user') {
    sessionStorage.setItem(scenarioKey, fromUrl)
    sessionStorage.removeItem(storageKey)
  }

  return (sessionStorage.getItem(scenarioKey) as MockScenario | null) ?? 'default'
}

export function loadDb(): MockDb {
  const scenario = currentScenario()
  const stored = sessionStorage.getItem(storageKey)
  return stored ? (JSON.parse(stored) as MockDb) : createSeed(scenario)
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
