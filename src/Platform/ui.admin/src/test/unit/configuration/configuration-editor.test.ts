import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { delay, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { useToast } from '@nuxt/ui/composables'
import {
  configureStoryteller,
  getStorytellerConfig,
  isNotFound,
  patchConfiguration,
  type StorytellerConfig,
} from '@42for.net/2splatform.sdk'
import ConfigurationEditor from '@/components/configuration/ConfigurationEditor.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'
import { annotationsOf, loadDb as loadMockDb } from '@/mock/db'
import { documentIds, putVersion } from '@/mock/documents'
import { ownContent } from '@/mock/configuration-handlers'
import { hasUnsavedChanges } from '@/composables/useUnsavedChanges'
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

  it('has no unsaved changes after deleting the own content (424-157)', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    expect(hasUnsavedChanges()).toBe(false)

    await view.host
      .findAll('button')
      .find((button) => button.text().includes('Delete own'))!
      .trigger('click')
    await settle()
    const input = document.body.querySelector<HTMLInputElement>(`[role="dialog"] input[placeholder="${key}"]`)!
    input.value = key
    input.dispatchEvent(new Event('input'))
    await settle()
    ;[...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')]
      .find((button) => button.textContent?.trim() === 'Delete')!
      .click()
    await settle(12)

    expect(ownContent(loadDb(), scope, key)).toBeUndefined()
    expect(view.host.text()).toContain('No configuration yet')
    expect(hasUnsavedChanges()).toBe(false)
    view.host.unmount()
  })
})

// 424-166: a save shows its toast after the patch, and a patch that ran out of time is checked, not guessed.
describe('ConfigurationEditor on a slow API', () => {
  let shared: StorytellerConfig | undefined

  /** The SDK of the unit tests has no timeout (test/setup.ts); these tests give it a short one. */
  function timeoutAfter(timeoutMs: number) {
    shared = getStorytellerConfig()
    configureStoryteller({ ...shared, timeoutMs })
  }

  // The toasts are global state of Nuxt UI: the toasts of the tests before would still be shown. These tests read the
  // toasts of their own save.
  beforeEach(() => {
    createApp({}).runWithContext(() => useToast().clear())
  })

  afterEach(() => {
    if (shared) {
      configureStoryteller(shared)
      shared = undefined
    }
  })

  const documentUrl = '*/api/v1/:org/:project/:view/configuration/:key'
  const versionUrl = '*/api/v1/:org/:project/:view/configuration/:key/versions/:version'
  const hang = async () => {
    await delay('infinite')
  }
  const page = () => document.body.textContent ?? ''

  async function edit(view: Awaited<ReturnType<typeof mountEditor>>) {
    const own = JSON.parse(textarea(view).element.value) as Record<string, unknown>
    const edited = { ...own, retries: own.retries === 7 ? 6 : 7 }
    await textarea(view).setValue(JSON.stringify(edited, null, 2))
    await settle()
    expect(view.host.text()).toContain('1 change')
    return { own, edited }
  }

  it('shows the toast of a save while the documents refresh in the background', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    const { edited } = await edit(view)
    // from now on the effective document does not answer
    server.use(http.get(documentUrl, hang))

    await saveButton(view).trigger('click')

    await vi.waitFor(() => expect(page()).toMatch(/Configuration saved \(v\d+\)/))
    expect(ownContent(loadDb(), scope, key)).toEqual(edited)
    expect(view.host.text()).not.toContain('1 change')
    expect(hasUnsavedChanges()).toBe(false)
    view.host.unmount()
  })

  it('counts a save that timed out as done when the content read again is the draft', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    const { edited } = await edit(view)
    timeoutAfter(200)
    // the API stores the patch, but its answer does not come in time
    server.use(
      http.patch(documentUrl, async () => {
        const db = loadDb()
        putVersion(db, scope, `configuration:${key}`, edited)
        saveDb(db)
        await delay('infinite')
      }),
    )

    await saveButton(view).trigger('click')

    await vi.waitFor(() => expect(page()).toContain('The API answered late, but the content is stored.'), {
      timeout: 3000,
    })
    expect(page()).toContain('Configuration saved')
    expect(page()).not.toContain('not saved')
    expect(view.host.text()).not.toContain('1 change')
    expect(hasUnsavedChanges()).toBe(false)
    view.host.unmount()
  })

  it('keeps the draft when a save that timed out is not stored (yet), and says it may still be', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    const { own } = await edit(view)
    timeoutAfter(200)
    server.use(http.patch(documentUrl, hang))

    await saveButton(view).trigger('click')

    // The content read again is the old one, but the API may still be applying the patch it no longer answers.
    await vi.waitFor(
      () =>
        expect(page()).toContain(
          'The API did not answer in time; the change may have been saved. Reload before saving again.',
        ),
      { timeout: 3000 },
    )
    expect(page()).not.toContain('Configuration saved')
    expect(ownContent(loadDb(), scope, key)).toEqual(own)
    expect(view.host.text()).toContain('1 change')
    expect(hasUnsavedChanges()).toBe(true)
    view.host.unmount()
  })

  it('says a save that timed out may have been stored when the content cannot be read again', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    await edit(view)
    timeoutAfter(200)
    server.use(http.patch(documentUrl, hang), http.get(documentUrl, hang))

    await saveButton(view).trigger('click')

    await vi.waitFor(
      () =>
        expect(page()).toContain(
          'The API did not answer in time; the change may have been saved. Reload before saving again.',
        ),
      { timeout: 3000 },
    )
    expect(page()).not.toContain('Configuration saved')
    expect(hasUnsavedChanges()).toBe(true)
    view.host.unmount()
  })

  it('shows the error of the stored content in the conflict dialog with Retry, not a spinner', async () => {
    const key = configuredExecution()
    const view = await mountEditor(key)
    const own = JSON.parse(textarea(view).element.value) as Record<string, unknown>
    const db = loadDb()
    putVersion(db, scope, `configuration:${key}`, { ...own, retries: 9 })
    saveDb(db)
    await textarea(view).setValue(JSON.stringify({ ...own, retries: own.retries === 2 ? 3 : 2 }, null, 2))
    await settle()
    // the stored content of the new version does not come
    timeoutAfter(200)
    server.use(http.get(versionUrl, hang))

    await saveButton(view).trigger('click')

    await vi.waitFor(() => expect(page()).toContain('The API did not answer in time'), { timeout: 3000 })
    expect(page()).toContain('Someone else changed this configuration')
    const button = (label: string) =>
      [...document.body.querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)
    expect(button('Retry')).toBeDefined()
    expect(button('Overwrite')?.disabled).toBe(true)
    expect(button('Reload, discard mine')?.disabled).toBe(true)

    // Retry reads the stored content again; this time it answers.
    server.resetHandlers()
    button('Retry')!.click()
    await vi.waitFor(() => expect(document.body.querySelector('[data-diff]')?.textContent).toContain('"retries": 9'), {
      timeout: 3000,
    })
    expect(button('Overwrite')?.disabled).toBe(false)
    view.host.unmount()
  })

  it('shows the error of the own content with Retry, not an empty document to patch', async () => {
    const key = configuredExecution()
    server.use(http.get(versionUrl, () => HttpResponse.json({ Message: 'Down' }, { status: 503 })))
    const view = await mountEditor(key)

    expect(view.host.find('textarea').exists()).toBe(false)
    expect(view.host.text()).toContain('The API failed')
    expect(view.host.text()).toContain('Down')

    server.resetHandlers()
    await view.host
      .findAll('button')
      .find((button) => button.text().includes('Retry'))!
      .trigger('click')
    await settle(10)

    expect(JSON.parse(textarea(view).element.value)).toEqual(ownContent(loadDb(), scope, key))
    view.host.unmount()
  })
})
