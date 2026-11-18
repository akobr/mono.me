import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import {
  acceptInvitation,
  createAccount,
  getAccessPoints,
  getAccount,
  getAnnotations,
  getExhibition,
  getProjects,
  joinExhibition,
  StorytellerError,
  type Account,
  type AccountRole,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import {
  createSeed,
  loadDb,
  mockUser,
  resetDb,
  saveDb,
  scenarios,
  storageKey,
  type MockDb,
  type MockScenario,
} from '@/mock/db'
import { groupMemberships } from '@/utils/memberships'

// The exhibition of the mock API (424-267, spec section 4): the demo project of the legacy datasets, GET
// /access/exhibition and POST /access/exhibition/join, and its organization in POST /access/account.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const legacyScenarios = scenarios.filter((scenario) => scenario !== 'showcase')
const projectKey = 'exhibition.exhibition'
const grace = 'user_01MOCKGRACE'
const seeded = createSeed('default').accessPoints
const stored = () => sessionStorage.getItem(storageKey)

/** Starts the scenario with changed data. */
function start(scenario: MockScenario, change: (db: MockDb) => void) {
  const db = resetDb(scenario)
  change(db)
  saveDb(db)
}

/** The user of the default dataset as a member of the exhibition project. */
const startAsMember = (role: AccountRole) =>
  start('default', (db) => {
    db.accessPoints[projectKey]!.AccessMap![mockUser.id] = role
    db.account!.AccessMap![projectKey] = role
  })

/** A 404 as the API answers it for a deployment without an exhibition: no body, so the error of the SDK has none. */
async function expectNotFound(request: Promise<unknown>) {
  const error: unknown = await request.catch((reason: unknown) => reason)
  expect(error).toBeInstanceOf(StorytellerError)
  expect(error).toMatchObject({ status: 404, body: undefined })
}

describe.each(legacyScenarios)('the exhibition of the %s dataset', (scenario) => {
  it('is the project exhibition.exhibition of Grace, with a description and without the user', () => {
    const db = createSeed(scenario)

    expect(db.exhibition).toEqual({ Organization: 'exhibition', Project: 'exhibition' })
    expect(db.accessPoints.exhibition?.AccessMap).toEqual({ [grace]: 'Owner' })
    expect(db.accessPoints.exhibition).not.toHaveProperty('Description')
    expect(db.accessPoints[projectKey]?.AccessMap).toEqual({ [grace]: 'Owner' })
    expect(db.accessPoints[projectKey]?.Description?.trim()).toBeTruthy()
  })

  it('answers GET /access/exhibition with its names and the description of the project', async () => {
    resetDb(scenario)

    expect(await getExhibition()).toEqual({
      Organization: 'exhibition',
      Project: 'exhibition',
      Description: seeded[projectKey]?.Description,
    })
  })
})

describe('the default dataset with the exhibition', () => {
  it('shows its user what it showed before: no membership, project or access point of the exhibition', async () => {
    const account = await getAccount()
    expect(account.AccessMap).toEqual({
      acme: 'Owner',
      'acme.billing': 'Owner',
      'acme.shipping': 'Contributor',
      'northwind.portal': 'Reader',
    })
    // /orgs lists the memberships of the account.
    expect(groupMemberships(account.AccessMap).map((organization) => organization.name)).toEqual(['acme', 'northwind'])
    expect((await getProjects()).map((project) => project.Key)).toEqual([
      'acme.billing',
      'acme.shipping',
      'northwind.portal',
    ])
    expect((await getAccessPoints()).map((point) => point.Key)).toEqual(['acme', 'acme.billing'])
    await expect(getAnnotations('exhibition', 'exhibition', 'default')).rejects.toMatchObject({ status: 403 })
  })
})

describe('GET /access/exhibition', () => {
  it('answers the project the setting names', async () => {
    start('new-user', (db) => {
      db.exhibition = { Organization: 'northwind', Project: 'portal' }
    })

    expect(await getExhibition()).toEqual({
      Organization: 'northwind',
      Project: 'portal',
      Description: seeded['northwind.portal']?.Description,
    })
  })

  it('answers no description for a project without one', async () => {
    start('new-user', (db) => {
      delete db.accessPoints[projectKey]!.Description
    })

    expect(await getExhibition()).toEqual({ Organization: 'exhibition', Project: 'exhibition' })
  })

  it('answers 404 without a body when the exhibition is turned off', async () => {
    start('new-user', (db) => {
      db.exhibition = null
    })

    await expectNotFound(getExhibition())
  })

  it('answers 404 without a body when the project of the exhibition does not exist', async () => {
    start('new-user', (db) => {
      delete db.accessPoints[projectKey]
    })

    await expectNotFound(getExhibition())
  })
})

describe('POST /access/exhibition/join', () => {
  it('creates the account of a user without one, with the Reader membership of the project only', async () => {
    resetDb('new-user')

    const account: Account = {
      Id: mockUser.id,
      UserName: mockUser.email,
      Name: mockUser.name,
      // No role in the organization of the exhibition.
      AccessMap: { [projectKey]: 'Reader' },
    }
    expect(await joinExhibition()).toEqual(account)

    const db = loadDb()
    expect(db.account).toEqual(account)
    expect(db.accessPoints[projectKey]?.AccessMap).toEqual({ [grace]: 'Owner', [mockUser.id]: 'Reader' })
    expect(db.accessPoints.exhibition?.AccessMap).toEqual({ [grace]: 'Owner' })
    expect(await getAccount()).toEqual(account)
    expect(await getProjects()).toEqual([
      { Key: projectKey, Role: 'Reader', Description: seeded[projectKey]?.Description, Settings: { Types: {}, Version: 0 } },
    ])
    expect(await getAccessPoints()).toEqual([])
  })

  it('lets the new Reader read the small catalog of the exhibition', async () => {
    resetDb('new-user')
    await joinExhibition()

    const { Annotations: annotations } = await getAnnotations('exhibition', 'exhibition', 'default')
    expect(annotations?.filter((annotation) => annotation.AnnotationType === 'Subject')).toHaveLength(3)
    expect(annotations?.every((annotation) => annotation.ProjectName === 'exhibition')).toBe(true)
  })

  it('changes nothing with a second join', async () => {
    resetDb('new-user')
    const first = await joinExhibition()
    const before = stored()

    expect(await joinExhibition()).toEqual(first)
    expect(stored()).toBe(before)
  })

  it('adds the membership to the account of a user who has one, and keeps the others', async () => {
    expect((await joinExhibition()).AccessMap).toEqual({
      acme: 'Owner',
      'acme.billing': 'Owner',
      'acme.shipping': 'Contributor',
      'northwind.portal': 'Reader',
      [projectKey]: 'Reader',
    })
    expect(loadDb().accessPoints[projectKey]?.AccessMap).toEqual({ [grace]: 'Owner', [mockUser.id]: 'Reader' })
  })

  it.each<AccountRole>(['Contributor', 'ContributorWithSecrets'])(
    'keeps the higher role of a user who is its %s',
    async (role) => {
      startAsMember(role)
      const before = stored()

      expect((await joinExhibition()).AccessMap?.[projectKey]).toBe(role)
      expect(loadDb().accessPoints[projectKey]?.AccessMap?.[mockUser.id]).toBe(role)
      expect(stored()).toBe(before)
    },
  )

  // As the API: a second Administrator or Owner of the project switches the exhibition off, without a write.
  it.each<AccountRole>(['Owner', 'Administrator'])(
    'is not offered while the user is a second manager of the project, as its %s',
    async (role) => {
      startAsMember(role)
      const before = stored()

      await expectNotFound(getExhibition())
      await expectNotFound(joinExhibition())
      expect(stored()).toBe(before)
    },
  )

  it('raises a membership without a role to Reader', async () => {
    startAsMember('None')

    expect((await joinExhibition()).AccessMap?.[projectKey]).toBe('Reader')
    expect(loadDb().accessPoints[projectKey]?.AccessMap?.[mockUser.id]).toBe('Reader')
  })

  it('joins the project the setting names', async () => {
    start('new-user', (db) => {
      db.exhibition = { Organization: 'northwind', Project: 'portal' }
    })

    expect((await joinExhibition()).AccessMap).toEqual({ 'northwind.portal': 'Reader' })
    expect(loadDb().accessPoints[projectKey]?.AccessMap).toEqual({ [grace]: 'Owner' })
  })

  it.each<[string, (db: MockDb) => void]>([
    [
      'the exhibition is turned off',
      (db) => {
        db.exhibition = null
      },
    ],
    [
      'the project of the exhibition does not exist',
      (db) => {
        delete db.accessPoints[projectKey]
      },
    ],
  ])('answers 404 without a body and writes nothing when %s', async (_state, change) => {
    start('new-user', change)
    const before = stored()

    await expectNotFound(joinExhibition())
    // No account, and no access point as the acceptance of an invitation creates a missing one.
    expect(stored()).toBe(before)
    expect(loadDb().account).toBeNull()
    await expect(getAccount()).rejects.toMatchObject({ status: 404 })
  })

  it('leaves the invitation of an invited user to accept, before or after the join', async () => {
    const memberships = { 'acme.billing': 'Contributor', [projectKey]: 'Reader' }

    resetDb('invited')
    await joinExhibition()
    expect((await acceptInvitation('inv_01MOCKBILLING')).AccessMap).toEqual(memberships)

    resetDb('invited')
    expect((await acceptInvitation('inv_01MOCKBILLING')).AccessMap).toEqual({ 'acme.billing': 'Contributor' })
    expect((await joinExhibition()).AccessMap).toEqual(memberships)
    expect(loadDb().accessPoints['acme.billing']?.AccessMap?.[mockUser.id]).toBe('Contributor')
  })
})

describe('POST /access/account', () => {
  // The organization form of onboarding takes any name: a user who created the organization of the exhibition would
  // own it, and the exhibition would be their project.
  it.each([
    ['its project', 'exhibition'],
    ['a new project', 'gallery'],
  ])('refuses the organization of the exhibition with %s to a user without an account', async (_what, project) => {
    resetDb('new-user')
    const before = stored()

    await expect(createAccount({ Organization: 'exhibition', Project: project })).rejects.toMatchObject({
      status: 409,
      errorCode: 'AccessPointExists',
    })
    // Nothing is written: no account, and Grace still owns the organization and the project.
    expect(stored()).toBe(before)
    expect(loadDb().account).toBeNull()
    expect(loadDb().accessPoints.exhibition?.AccessMap).toEqual({ [grace]: 'Owner' })
    expect(loadDb().accessPoints[projectKey]?.AccessMap).toEqual({ [grace]: 'Owner' })
  })
})

describe('the showcase dataset', () => {
  it('has no exhibition: both routes answer 404 without a body and write nothing', async () => {
    expect(resetDb('showcase').exhibition).toBeUndefined()
    const before = stored()

    await expectNotFound(getExhibition())
    await expectNotFound(joinExhibition())
    expect(stored()).toBe(before)
  })
})
