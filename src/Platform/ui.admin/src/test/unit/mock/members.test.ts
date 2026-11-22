import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import {
  getAccount,
  getExhibition,
  joinExhibition,
  removeMember,
  StorytellerError,
  type AccountRole,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, mockUser, resetDb, saveDb } from '@/mock/db'
import { setStaleSignIn } from '@/mock/step-up'

// The removal of a member in the mock API, as the API's RemoveMemberAsync decides it (424-271): any member may leave,
// without a recent sign-in; removing someone else needs Administrator, and Owner for an Owner; the last Owner stays.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => {
  resetDb('default')
})
afterEach(() => {
  server.resetHandlers()
  setStaleSignIn(false)
})
afterAll(() => server.close())

const ada = mockUser.id
const grace = 'user_01MOCKGRACE'
const exhibition = 'exhibition.exhibition'

/** The user of the default dataset with another role on the access point. */
function giveRole(key: string, role: AccountRole) {
  const db = loadDb()
  db.accessPoints[key]!.AccessMap![ada] = role
  db.account!.AccessMap![key] = role
  saveDb(db)
}

/** The error of a call that must fail, with its status and error code. */
async function failure(action: () => Promise<unknown>): Promise<StorytellerError> {
  const error: unknown = await action().then(
    () => undefined,
    (reason: unknown) => reason,
  )
  expect(error).toBeInstanceOf(StorytellerError)
  return error as StorytellerError
}

describe('leaving an access point in the mock API', () => {
  it.each([
    ['a Reader', 'northwind.portal', undefined],
    ['a Contributor', 'acme.shipping', undefined],
    ['an Administrator', 'acme.shipping', 'Administrator'],
    ['a ContributorWithSecrets', 'acme.shipping', 'ContributorWithSecrets'],
  ] as const)('lets %s leave and keeps the other memberships', async (_who, key, role) => {
    if (role) {
      giveRole(key, role)
    }
    const before = (await getAccount()).AccessMap!

    await expect(removeMember(key, ada)).resolves.toBeUndefined()

    const { [key]: _left, ...others } = before
    expect((await getAccount()).AccessMap).toEqual(others)
    expect(loadDb().accessPoints[key]!.AccessMap).not.toHaveProperty(ada)
  })

  it('lets a member leave with a stale sign-in', async () => {
    setStaleSignIn(true)

    await expect(removeMember('acme.shipping', ada)).resolves.toBeUndefined()
  })

  it('refuses the last Owner, who stays', async () => {
    const error = await failure(() => removeMember('acme.billing', ada))

    expect(error.status).toBe(409)
    expect(error.body).toMatchObject({ ErrorCode: 'LastOwner' })
    expect((await getAccount()).AccessMap).toHaveProperty(['acme.billing'], 'Owner')
  })

  it('lets an Owner leave while another Owner stays', async () => {
    const db = loadDb()
    db.accessPoints['acme.billing']!.AccessMap![grace] = 'Owner'
    saveDb(db)

    await expect(removeMember('acme.billing', ada)).resolves.toBeUndefined()
    expect(loadDb().accessPoints['acme.billing']!.AccessMap).toMatchObject({ [grace]: 'Owner' })
  })

  it('answers MemberNotFound to a caller who left already', async () => {
    await removeMember('acme.shipping', ada)

    const error = await failure(() => removeMember('acme.shipping', ada))

    expect(error.status).toBe(404)
    expect(error.body).toMatchObject({ ErrorCode: 'MemberNotFound' })
  })

  it('records the leave in the access audit, with the member as the target', async () => {
    await removeMember('northwind.portal', ada)

    expect(loadDb().accessAudit?.['northwind']?.at(-1)).toMatchObject({ Action: 'MemberRemoved', OldValue: 'Reader' })
  })
})

describe('removing another member in the mock API', () => {
  it('refuses a Contributor, before it looks for the member', async () => {
    const error = await failure(() => removeMember('acme.shipping', grace))
    const missing = await failure(() => removeMember('acme.shipping', 'user_01MOCKNOBODY'))

    expect(error.status).toBe(403)
    expect(error.body).toMatchObject({ ErrorCode: 'AccessDenied' })
    expect(missing.status).toBe(403)
    expect(loadDb().accessPoints['acme.shipping']!.AccessMap).toHaveProperty(grace, 'Owner')
  })

  it('answers MemberNotFound to an Administrator for an account that is no member', async () => {
    giveRole('acme.shipping', 'Administrator')

    const error = await failure(() => removeMember('acme.shipping', 'user_01MOCKNOBODY'))

    expect(error.status).toBe(404)
    expect(error.body).toMatchObject({ ErrorCode: 'MemberNotFound' })
  })

  it('refuses an Administrator who removes an Owner, as the API does', async () => {
    giveRole('acme.shipping', 'Administrator')
    // another Owner, so the last Owner rule does not decide
    const db = loadDb()
    db.accessPoints['acme.shipping']!.AccessMap!['user_01MOCKALAN'] = 'Owner'
    saveDb(db)

    const error = await failure(() => removeMember('acme.shipping', grace))

    expect(error.status).toBe(403)
    expect(error.body).toMatchObject({ ErrorCode: 'AccessDenied' })
    expect(loadDb().accessPoints['acme.shipping']!.AccessMap).toHaveProperty(grace, 'Owner')
  })

  it('lets an Owner remove another Owner', async () => {
    const db = loadDb()
    db.accessPoints['acme.billing']!.AccessMap![grace] = 'Owner'
    saveDb(db)

    await expect(removeMember('acme.billing', grace)).resolves.toBeUndefined()
    expect(loadDb().accessPoints['acme.billing']!.AccessMap).not.toHaveProperty(grace)
  })

  it('lets an Owner remove another member', async () => {
    await expect(removeMember('acme.billing', 'user_01MOCKALAN')).resolves.toBeUndefined()
    expect(loadDb().accessPoints['acme.billing']!.AccessMap).not.toHaveProperty('user_01MOCKALAN')
  })
})

describe('leaving the exhibition in the mock API', () => {
  it('keeps the exhibition offered, and a second join adds the membership again', async () => {
    await joinExhibition()
    expect((await getAccount()).AccessMap).toHaveProperty([exhibition], 'Reader')

    await removeMember(exhibition, ada)

    const account = await getAccount()
    expect(account.AccessMap).not.toHaveProperty([exhibition])
    // the account stays, with its other memberships
    expect(account.AccessMap).toHaveProperty(['acme.billing'], 'Owner')
    await expect(getExhibition()).resolves.toMatchObject({ Organization: 'exhibition', Project: 'exhibition' })
    expect((await joinExhibition()).AccessMap).toHaveProperty([exhibition], 'Reader')
  })
})
