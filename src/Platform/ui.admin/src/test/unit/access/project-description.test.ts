import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import {
  createAccessPoint,
  createAccount,
  getAccessPoint,
  getProjects,
  setMachineAuthentication,
  setMemberRole,
  updateAccessPoint,
  type AccessPointUpdate,
  type AccountRole,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, resetDb, saveDb } from '@/mock/db'
import { showcaseProjects } from '@/mock/showcase'

// The description of a project in the mock API (424-78, spec section 6): on creation, PUT /access/points/{key} and
// GET /access/projects, with the rules of the API (trimmed, a blank one is none, only a project has one).

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const seeded = createSeed('default').accessPoints

/** A PUT with a body the SDK does not send. */
const putBody = (key: string, body: string) =>
  fetch(`http://api.test/api/v1/access/points/${key}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body,
  })

describe('creating a project with a description', () => {
  it('stores it trimmed on the project, and none on the organization it creates', async () => {
    const point = await createAccessPoint({
      Organization: 'globex',
      Project: 'portal',
      ProjectDescription: '  The customer portal of Globex.  ',
    })

    expect(point.Description).toBe('The customer portal of Globex.')
    const db = loadDb()
    expect(db.accessPoints['globex.portal']?.Description).toBe('The customer portal of Globex.')
    expect(db.accessPoints.globex).toBeDefined()
    expect(db.accessPoints.globex).not.toHaveProperty('Description')
  })

  it('stores no blank description', async () => {
    const point = await createAccessPoint({ Organization: 'acme', Project: 'returns', ProjectDescription: '   ' })

    expect(point).not.toHaveProperty('Description')
    expect(loadDb().accessPoints['acme.returns']).not.toHaveProperty('Description')
  })

  it('stores the description of the first project on onboarding, not on the organization', async () => {
    resetDb('new-user')
    await createAccount({ Organization: 'globex', Project: 'portal', ProjectDescription: ' The customer portal. ' })

    const db = loadDb()
    expect(db.accessPoints['globex.portal']?.Description).toBe('The customer portal.')
    expect(db.accessPoints.globex).not.toHaveProperty('Description')
  })

  it('stores no blank description on onboarding', async () => {
    resetDb('new-user')
    await createAccount({ Organization: 'globex', Project: 'portal', ProjectDescription: '  ' })

    expect(loadDb().accessPoints['globex.portal']).not.toHaveProperty('Description')
  })
})

describe('PUT /access/points/{key}', () => {
  it('sets, replaces and clears the description of a project, and keeps its members and policy', async () => {
    const before = await getAccessPoint('acme.billing')

    const set = await updateAccessPoint('acme.billing', { Description: '  Invoices of Acme.  ' })
    expect(set).toEqual({ ...before, Description: 'Invoices of Acme.' })
    expect((await getAccessPoint('acme.billing')).Description).toBe('Invoices of Acme.')
    expect((await updateAccessPoint('acme.billing', { Description: 'Invoices and payments.' })).Description).toBe(
      'Invoices and payments.',
    )

    const clears: AccessPointUpdate[] = [{ Description: '   ' }, {}, { Description: null as unknown as string }]
    for (const update of clears) {
      await updateAccessPoint('acme.billing', { Description: 'Invoices.' })
      const cleared = await updateAccessPoint('acme.billing', update)
      expect(cleared, JSON.stringify(update)).not.toHaveProperty('Description')
      expect(cleared.AccessMap).toEqual(before.AccessMap)
      expect(loadDb().accessPoints['acme.billing'], JSON.stringify(update)).not.toHaveProperty('Description')
    }
  })

  it('clears the description with an empty body or null, as the API does', async () => {
    expect(loadDb().accessPoints['acme.billing']?.Description).toBe(seeded['acme.billing']?.Description)

    // The SDK sends no body without an update.
    expect(await updateAccessPoint('acme.billing')).not.toHaveProperty('Description')
    expect(loadDb().accessPoints['acme.billing']).not.toHaveProperty('Description')

    await updateAccessPoint('acme.billing', { Description: 'Invoices.' })
    const response = await putBody('acme.billing', 'null')
    expect(response.status).toBe(200)
    expect(loadDb().accessPoints['acme.billing']).not.toHaveProperty('Description')
  })

  it('refuses a Contributor, an organization key and a body that is not JSON, and changes nothing', async () => {
    // The user contributes to acme.shipping and owns acme and acme.billing.
    await expect(updateAccessPoint('acme.shipping', { Description: 'Parcels.' })).rejects.toMatchObject({
      status: 403,
      errorCode: 'AccessDenied',
    })
    await expect(updateAccessPoint('acme', { Description: 'The Acme company.' })).rejects.toMatchObject({
      status: 400,
      message: 'Access point key must be in organization.project format.',
    })
    for (const body of ['{ "Description": ', 'Invoices.', '["Invoices."]', '{ "Description": 42 }']) {
      const response = await putBody('acme.billing', body)
      expect(response.status, body).toBe(400)
      expect(await response.json()).toMatchObject({ Message: 'The request body is not a project update.' })
    }

    const points = loadDb().accessPoints
    expect(points['acme.shipping']?.Description).toBe(seeded['acme.shipping']?.Description)
    expect(points['acme.billing']?.Description).toBe(seeded['acme.billing']?.Description)
    expect(points.acme).not.toHaveProperty('Description')
  })

  it('answers 404 for a project that does not exist', async () => {
    const db = loadDb()
    db.account!.AccessMap!['acme.gone'] = 'Owner'
    saveDb(db)

    await expect(updateAccessPoint('acme.gone', { Description: 'Gone.' })).rejects.toMatchObject({
      status: 404,
      errorCode: 'NotFound',
    })
    expect(loadDb().accessPoints['acme.gone']).toBeUndefined()
  })

  it('keeps the description through a member change and a machine policy write', async () => {
    await updateAccessPoint('acme.billing', { Description: 'Invoices of Acme.' })
    await setMemberRole('acme.billing', 'user_01MOCKALAN', { Role: 'Contributor' })
    await setMachineAuthentication('acme.billing', { EnabledCredentialKinds: ['ApiKey', 'Certificate'] })

    const point = await getAccessPoint('acme.billing')
    expect(point.AccessMap?.user_01MOCKALAN).toBe('Contributor')
    expect(point.MachineAuthentication?.EnabledCredentialKinds).toEqual(['ApiKey', 'Certificate'])
    expect(point.Description).toBe('Invoices of Acme.')
  })
})

describe('GET /access/projects', () => {
  it('lists the projects of every role with their descriptions, ordered by key, without organizations', async () => {
    expect(await getProjects()).toEqual([
      { Key: 'acme.billing', Role: 'Owner', Description: seeded['acme.billing']?.Description },
      { Key: 'acme.shipping', Role: 'Contributor', Description: seeded['acme.shipping']?.Description },
      { Key: 'northwind.portal', Role: 'Reader', Description: seeded['northwind.portal']?.Description },
    ])
  })

  it('lists a changed description, and a project without one without it', async () => {
    await updateAccessPoint('acme.billing', { Description: 'Invoices of Acme.' })
    await createAccessPoint({ Organization: 'acme', Project: 'returns' })

    const projects = await getProjects()
    expect(projects.find((project) => project.Key === 'acme.billing')?.Description).toBe('Invoices of Acme.')
    expect(projects.find((project) => project.Key === 'acme.returns')).toEqual({ Key: 'acme.returns', Role: 'Owner' })
  })

  it('lists every showcase project with its description, in each of Ada’s roles', async () => {
    resetDb('showcase')

    const projects = await getProjects()
    expect(projects.map((project) => project.Key)).toEqual(
      showcaseProjects.map((project) => `${project.org}.${project.project}`).toSorted(),
    )
    for (const project of showcaseProjects) {
      expect(projects.find((summary) => summary.Key === `${project.org}.${project.project}`)?.Description).toBe(
        project.description,
      )
    }
    expect(new Set(projects.map((project) => project.Role))).toEqual(
      new Set<AccountRole>(['Reader', 'Contributor', 'ContributorWithSecrets', 'Administrator', 'Owner']),
    )
  })

  it('answers 404 without an account, and lists the first project after onboarding', async () => {
    resetDb('new-user')
    await expect(getProjects()).rejects.toMatchObject({ status: 404, errorCode: 'NotFound' })

    await createAccount({ Organization: 'globex', Project: 'portal', ProjectDescription: 'The customer portal.' })
    expect(await getProjects()).toEqual([{ Key: 'globex.portal', Role: 'Owner', Description: 'The customer portal.' }])
  })
})
