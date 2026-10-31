import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { isNotFound, patchConfiguration } from '@42for.net/2splatform.sdk'
import ConfigurationEditor from '@/components/configuration/ConfigurationEditor.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'
import { annotationsOf, loadDb as loadMockDb } from '@/mock/db'
import { documentIds, putVersion } from '@/mock/documents'
import { ownContent } from '@/mock/configuration-handlers'
import { mountScreen, settle } from '../../helpers'

// The editor against the mock API of the e2e mode: the same handlers the browser uses, served by msw/node.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }

function configuredExecution(): string {
  const id = documentIds(loadDb(), scope, 'configuration:').find((entry) => entry.startsWith('configuration:exe.'))!
  return id.slice('configuration:'.length)
}

/** An execution of the seed without a stored configuration: GET answers 200 with Version 0, the version 0 is a 404. */
function unconfiguredExecution(): string {
  const db = loadMockDb()
  const configured = new Set(documentIds(db, scope, 'configuration:').map((id) => id.slice('configuration:'.length)))
  return [...annotationsOf(db, 'acme', 'billing', 'default').keys()].find(
    (key) => key.startsWith('exe.') && !configured.has(key),
  )!
}

async function mountEditor(key: string) {
  const view = await mountScreen(
    ConfigurationEditor,
    { annotationKey: key },
    { path: `/acme/billing/default/configurations/${key}?format=json` },
  )
  await settle(10)
  return view
}

const textarea = (view: Awaited<ReturnType<typeof mountEditor>>) => view.host.find('textarea')
const saveButton = (view: Awaited<ReturnType<typeof mountEditor>>) =>
  view.host.findAll('button').find((button) => button.text().startsWith('Save'))!

describe('ConfigurationEditor (JSON Patch saves)', () => {
  it('loads the own content and saves a guarded patch as a new version', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    const own = JSON.parse(textarea(view).element.value) as Record<string, unknown>

    expect(own).toEqual(ownContent(loadDb(), scope, key))

    await textarea(view).setValue(JSON.stringify({ ...own, retries: 7 }, null, 2))
    await settle()
    expect(view.host.text()).toContain('1 change')

    await saveButton(view).trigger('click')
    await settle(10)

    expect(ownContent(loadDb(), scope, key)?.retries).toBe(7)
    expect(view.host.text()).not.toContain('1 change')
    view.host.unmount()
  })

  it('offers the create flow when nothing is stored yet, instead of a patch that would fail', async () => {
    const key = unconfiguredExecution()
    const view = await mountEditor(key)

    expect(view.host.text()).toContain('No configuration yet')

    await view.host
      .findAll('button')
      .find((button) => button.text().includes('Create empty'))!
      .trigger('click')
    await settle(10)

    expect(ownContent(loadDb(), scope, key)).toEqual({})

    // The empty document is a stored version, so the first save patches it.
    await textarea(view).setValue(JSON.stringify({ retries: 2 }, null, 2))
    await settle()
    await saveButton(view).trigger('click')
    await settle(10)

    expect(ownContent(loadDb(), scope, key)).toEqual({ retries: 2 })
    view.host.unmount()
  })

  it('stores a version for a PUT, as the API does for POST and PUT', async () => {
    const key = unconfiguredExecution()

    const response = await fetch(
      `http://api.test/api/v1/${scope.org}/${scope.project}/${scope.view}/configuration/${key}`,
      {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', Authorization: 'Bearer test-token' },
        body: JSON.stringify({ retries: 2 }),
      },
    )

    expect(response.status).toBe(200)
    expect(ownContent(loadDb(), scope, key)).toEqual({ retries: 2 })
  })

  it('refuses a PUT that fails the schema with 409, and stores it with force=true', async () => {
    const key = unconfiguredExecution()
    const url = `http://api.test/api/v1/${scope.org}/${scope.project}/${scope.view}/configuration/${key}`
    const put = (query = '') =>
      fetch(url + query, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', Authorization: 'Bearer test-token' },
        body: JSON.stringify({ retries: 20 }),
      })

    const refused = await put()
    expect(refused.status).toBe(409)
    expect(ownContent(loadDb(), scope, key)).toBeUndefined()

    expect((await put('?force=true')).status).toBe(200)
    expect(ownContent(loadDb(), scope, key)).toEqual({ retries: 20 })
  })

  it('gets a 404 for a patch when nothing is stored, as from the API', async () => {
    const key = unconfiguredExecution()

    const error = await patchConfiguration(scope.org, scope.project, scope.view, key, [
      { op: 'add', path: '/retries', value: 2 },
    ]).catch((reason: unknown) => reason)

    expect(isNotFound(error)).toBe(true)
    expect(ownContent(loadDb(), scope, key)).toBeUndefined()
  })

  it('shows the 409 schema errors with Save anyway', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    const own = JSON.parse(textarea(view).element.value) as Record<string, unknown>

    await textarea(view).setValue(JSON.stringify({ ...own, retries: 20 }, null, 2))
    await settle()
    await saveButton(view).trigger('click')
    await settle(10)

    expect(view.host.text()).toContain('The configuration does not match its schema')
    expect(view.host.text()).toContain('/retries')
    expect(view.host.text()).toContain('Number too big')
    expect(view.host.text()).toContain('Save anyway')
    view.host.unmount()
  })

  it('opens the conflict dialog when a test operation fails (412)', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    const own = JSON.parse(textarea(view).element.value) as Record<string, unknown>

    // Someone else saves meanwhile.
    const db = loadDb()
    putVersion(db, scope, `configuration:${key}`, { ...own, retries: 9 })
    saveDb(db)

    await textarea(view).setValue(JSON.stringify({ ...own, retries: own.retries === 2 ? 3 : 2 }, null, 2))
    await settle()
    await saveButton(view).trigger('click')
    await settle(12)

    expect(document.body.textContent).toContain('Someone else changed this configuration')
    expect(document.body.querySelector('[data-diff]')?.textContent).toContain('"retries": 9')
    view.host.unmount()
  })
})
