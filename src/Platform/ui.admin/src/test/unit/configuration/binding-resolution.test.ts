import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { getResolvedConfiguration, StorytellerError } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { storeAnnotation } from '@/mock/annotation-handlers'
import { annotationsOf, loadDb, saveDb } from '@/mock/db'
import { putVersion } from '@/mock/documents'

// The Resolved read of the mock API evaluates @config("…"), @annotation("…"[, "<Type>"]) and "@<key>/<path>", and
// leaves sourced values (secrets) as written (424-77).

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const params = { org: 'acme', project: 'billing', view: 'default' }
const execution = 'exe.northwind.invoicing.prod'

function configure(content: Record<string, unknown>) {
  const db = loadDb()
  const subject = annotationsOf(db, params.org, params.project, params.view).get('sbt.northwind')!
  storeAnnotation(db, params, { ...subject, Values: { tier: 'gold', contact: { email: 'ops@northwind.example' } } })
  putVersion(db, params, 'configuration:cnt.northwind.prod', { baseUrl: 'https://prod.northwind.example' })
  putVersion(db, params, `configuration:${execution}`, content)
  saveDb(db)
}

const resolve = () => getResolvedConfiguration(params.org, params.project, params.view, execution)

describe('the bindings of the mock Resolved read', () => {
  it('reads the configuration being resolved, by JSON Pointer and by JSONPath', async () => {
    configure({
      endpoint: '@config("/baseUrl")',
      fallback: '@config("$.baseUrl")',
      timeout: 30,
      retry: '@config("/timeout")',
    })
    expect((await resolve()).Content).toMatchObject({
      endpoint: 'https://prod.northwind.example',
      fallback: 'https://prod.northwind.example',
      retry: 30,
    })
  })

  it('reads the Values of the annotation’s ancestor of a type', async () => {
    configure({ tier: '@annotation("/tier", "Subject")', contact: '@annotation("$.contact.email", "subject")' })
    expect((await resolve()).Content).toMatchObject({ tier: 'gold', contact: 'ops@northwind.example' })
  })

  it('still reads another annotation’s document with @<key>/<path>', async () => {
    configure({ endpoint: '@cnt.northwind.prod/baseUrl' })
    expect((await resolve()).Content).toMatchObject({ endpoint: 'https://prod.northwind.example' })
  })

  it('leaves a secret as written', async () => {
    configure({ password: '@(db.password, primaryVault)' })
    expect((await resolve()).Content).toMatchObject({ password: '@(db.password, primaryVault)' })
  })

  it('answers 422 for a binding it cannot resolve', async () => {
    configure({ owner: '@annotation("/owner")' })
    const failure = await resolve().catch((error: unknown) => error)
    expect(failure).toBeInstanceOf(StorytellerError)
    expect((failure as StorytellerError).status).toBe(422)
    expect((failure as StorytellerError).errorCode).toBe('BindingFailed')
  })
})
