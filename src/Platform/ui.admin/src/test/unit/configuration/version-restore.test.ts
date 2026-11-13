import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { useToast } from '@nuxt/ui/composables'
import {
  deleteAnnotation,
  deleteConfiguration,
  deleteTemplate,
  getAnnotationHistory,
  getConfiguration,
  getConfigurations,
  getConfigurationVersions,
  getTemplate,
  getTemplates,
  getTemplateVersions,
  purgeAnnotation,
  restoreAnnotationSchemaVersion,
  restoreConfigurationVersion,
  restoreDescendantTypeSchemaVersion,
  restoreSchemaVersion,
  restoreTemplateVersion,
  StorytellerError,
  type ConfigurationVersion,
  type VersionRestoreResult,
} from '@42for.net/2splatform.sdk'
import ConfigurationVersions from '@/components/configuration/ConfigurationVersions.vue'
import TemplateDetail from '@/components/templates/TemplateDetail.vue'
import VersionTimeline from '@/components/versions/VersionTimeline.vue'
import VersionsView from '@/components/versions/VersionsView.vue'
import type { VersionSource } from '@/components/versions/version-source'
import { ownContent } from '@/mock/configuration-handlers'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'
import { documentIds, getDocument, getHistory, latest, putVersion, type StoredDocument } from '@/mock/documents'
import { mountScreen, settle } from '../../helpers'

// The restore of a previous version of a configuration, a template or a schema (424-221): the mock API's rules, the
// Restore of the history with its confirmation, its toasts and Restore anyway, and the versions of a deleted document.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }
const { org, project, view } = scope
const stamp = {
  At: '2026-11-01T08:00:00Z',
  By: { Kind: 'Account' as const, Id: 'user_01MOCKADA', Name: 'Ada Lovelace' },
}

/**
 * A configured execution of the seed with three more versions: one that complies with its schema, one that does not
 * (retries above every maximum), and the current one.
 */
function configuredExecution() {
  const db = loadDb()
  const key = documentIds(db, scope, 'configuration:')
    .find((id) => id.startsWith('configuration:exe.'))!
    .slice('configuration:'.length)
  const id = `configuration:${key}` as const
  const valid = putVersion(db, scope, id, { retries: 4 }).Version
  const invalid = putVersion(db, scope, id, { retries: 20 }).Version
  const current = putVersion(db, scope, id, { retries: 5 }).Version
  saveDb(db)
  return { key, valid, invalid, current }
}

function setRole(role: string) {
  const db = loadDb()
  db.account!.AccessMap!['acme.billing'] = role as never
  saveDb(db)
}

/** The toasts are global state of Nuxt UI: each test reads the toasts of its own restore. */
const clearToasts = () => createApp({}).runWithContext(() => useToast().clear())

const restored = (key: string) =>
  getAnnotationHistory(org, project, view, key).then((history) =>
    (history.Entries ?? []).filter((entry) => entry.Action === 'ConfigurationRestored'),
  )

describe('the restore of a configuration version in the mock API', () => {
  it('stores the version as the next one, with RestoredFrom and its audit entry', async () => {
    const { key, valid, current } = configuredExecution()

    const result = await restoreConfigurationVersion(org, project, view, key, valid)

    expect(result).toMatchObject({ Outcome: 'Restored', FromVersion: valid, Version: current + 1 })
    expect(result.Modified?.By?.Id).toBe('user_01MOCKADA')
    expect(ownContent(loadDb(), scope, key)).toEqual({ retries: 4 })
    const versions = await getConfigurationVersions(org, project, view, key)
    expect(versions.at(-1)).toMatchObject({ Version: current + 1, RestoredFrom: valid })
    expect(versions.at(-2)?.RestoredFrom).toBeUndefined()
    expect(await restored(key)).toEqual([
      expect.objectContaining({ ConfigurationVersion: current + 1, FromVersion: valid }),
    ])
  })

  it('changes nothing for the current version or a version with its content', async () => {
    const { key, valid, current } = configuredExecution()

    expect(await restoreConfigurationVersion(org, project, view, key, current)).toMatchObject({
      Outcome: 'Unchanged',
      FromVersion: current,
      Version: current,
    })
    await restoreConfigurationVersion(org, project, view, key, valid)
    // the current version has the content of `valid` now
    expect(await restoreConfigurationVersion(org, project, view, key, valid)).toMatchObject({
      Outcome: 'Unchanged',
      FromVersion: valid,
      Version: current + 1,
    })

    expect(latest(getDocument(loadDb(), scope, `configuration:${key}`))?.Version).toBe(current + 1)
    expect(await restored(key)).toHaveLength(1)
  })

  it('checks the version against the schema unless force=true', async () => {
    const { key, invalid, current } = configuredExecution()

    const refusal = await restoreConfigurationVersion(org, project, view, key, invalid).catch((error: unknown) => error)
    expect(refusal).toMatchObject({ status: 409, errorCode: 'SchemaValidationFailed' })
    expect((refusal as StorytellerError).schemaErrors?.[0]?.AnnotationKey).toBe(key)
    expect(latest(getDocument(loadDb(), scope, `configuration:${key}`))?.Version).toBe(current)

    expect(await restoreConfigurationVersion(org, project, view, key, invalid, { force: true })).toMatchObject({
      Outcome: 'Restored',
      Version: current + 1,
    })
    expect(ownContent(loadDb(), scope, key)).toEqual({ retries: 20 })
  })

  it('does not check an empty version against the schema, as the API and every write', async () => {
    const db = loadDb()
    const key = documentIds(db, scope, 'configuration:')
      .find((id) => id.startsWith('configuration:exe.'))!
      .slice('configuration:'.length)
    const id = `configuration:${key}` as const
    // the schema of the execution type requires retries, which the empty version does not have
    const empty = putVersion(db, scope, id, {}).Version
    const current = putVersion(db, scope, id, { retries: 4 }).Version
    saveDb(db)

    expect(await restoreConfigurationVersion(org, project, view, key, empty)).toMatchObject({
      Outcome: 'Restored',
      FromVersion: empty,
      Version: current + 1,
    })
    expect(ownContent(loadDb(), scope, key)).toEqual({})
  })

  it('refuses an unknown or expired version and a Reader', async () => {
    const { key, valid } = configuredExecution()
    const db = loadDb()
    const stored = db.documents!['acme/billing/default']![`configuration:${key}`] as StoredDocument
    stored.versions.find((entry) => entry.Version === valid)!.ExpirationTime = new Date(Date.now() - 1000).toISOString()
    saveDb(db)

    for (const version of [999, valid]) {
      await expect(restoreConfigurationVersion(org, project, view, key, version)).rejects.toMatchObject({
        status: 404,
        errorCode: 'VersionNotFound',
      })
    }
    setRole('Reader')
    await expect(restoreConfigurationVersion(org, project, view, key, valid - 1)).rejects.toMatchObject({ status: 403 })
  })

  it('refuses a deleted annotation (409), and a missing or purged one (404)', async () => {
    const { key, valid } = configuredExecution()
    await expect(restoreConfigurationVersion(org, project, view, 'exe.nobody.none.prod', 1)).rejects.toMatchObject({
      status: 404,
      errorCode: 'AnnotationNotFound',
    })

    await deleteAnnotation(org, project, view, key)
    await expect(restoreConfigurationVersion(org, project, view, key, valid)).rejects.toMatchObject({
      status: 409,
      errorCode: 'AnnotationDeleted',
    })
    // as the API: the versions of a deleted annotation are hidden until it is restored
    await expect(getConfigurationVersions(org, project, view, key)).rejects.toMatchObject({
      status: 404,
      errorCode: 'AnnotationDeleted',
    })

    await purgeAnnotation(org, project, view, key)
    await expect(restoreConfigurationVersion(org, project, view, key, valid)).rejects.toMatchObject({
      status: 404,
      errorCode: 'AnnotationNotFound',
    })
    // a purged annotation keeps its versions until they expire
    expect((await getConfigurationVersions(org, project, view, key)).map((entry) => entry.Version)).toContain(valid)
  })

  it('brings a deleted configuration back, continuing its numbering', async () => {
    const { key, valid, current } = configuredExecution()
    const before = await getConfigurationVersions(org, project, view, key)

    await deleteConfiguration(org, project, view, key)
    const listed = async () =>
      (await getConfigurations(org, project, view)).Configurations!.map((entry) => entry.AnnotationKey)
    expect(await listed()).not.toContain(key)
    expect(await getConfigurationVersions(org, project, view, key)).toEqual(before)
    expect((await getConfiguration(org, project, view, key)).Version).toBe(0)

    expect(await restoreConfigurationVersion(org, project, view, key, valid)).toMatchObject({
      Outcome: 'Restored',
      Version: current + 1,
    })
    expect((await getConfiguration(org, project, view, key)).Version).toBe(current + 1)
    expect(await listed()).toContain(key)
  })
})

describe('the restore of a template or schema version in the mock API', () => {
  it('restores a template version, also after the template was deleted', async () => {
    expect(await restoreTemplateVersion(org, project, view, 'exe', 1)).toMatchObject({
      Outcome: 'Restored',
      FromVersion: 1,
      Version: 4,
    })
    expect((await getTemplate(org, project, view, 'exe')).Content?.retries).toBe(1)
    expect((await getTemplateVersions(org, project, view, 'exe')).at(-1)).toMatchObject({ Version: 4, RestoredFrom: 1 })
    expect(await restoreTemplateVersion(org, project, view, 'exe', 4)).toMatchObject({
      Outcome: 'Unchanged',
      Version: 4,
    })
    expect(await restoreTemplateVersion(org, project, view, 'exe', 1)).toMatchObject({
      Outcome: 'Unchanged',
      Version: 4,
    })

    await deleteTemplate(org, project, view, 'exe')
    const types = async () => (await getTemplates(org, project, view)).map((entry) => entry.AnnotationType)
    expect(await types()).not.toContain('exe')
    expect(getHistory(loadDb(), scope, 'template:exe')?.deleted).toBe(true)
    expect(await getTemplateVersions(org, project, view, 'exe')).toHaveLength(4)

    expect(await restoreTemplateVersion(org, project, view, 'exe', 2)).toMatchObject({
      Outcome: 'Restored',
      Version: 5,
    })
    expect((await getTemplate(org, project, view, 'exe')).Content?.retries).toBe(2)
    expect(await types()).toContain('exe')
  })

  it('checks a restored schema against the stored configurations unless force=true', async () => {
    const db = loadDb()
    const id = 'schema:type:exe' as const
    const schema = latest(getDocument(db, scope, id))!.Content!
    const strict = structuredClone(schema) as { properties: Record<string, unknown> }
    strict.properties.retries = { type: 'integer', maximum: 0 }
    const strictVersion = putVersion(db, scope, id, strict).Version
    putVersion(db, scope, id, schema)
    saveDb(db)

    const refusal = await restoreSchemaVersion(org, project, view, 'exe', strictVersion).catch(
      (error: unknown) => error,
    )
    expect(refusal).toMatchObject({ status: 409, errorCode: 'SchemaValidationFailed' })
    expect((refusal as StorytellerError).schemaErrors?.length).toBeGreaterThan(0)

    expect(await restoreSchemaVersion(org, project, view, 'exe', strictVersion, { force: true })).toMatchObject({
      Outcome: 'Restored',
      FromVersion: strictVersion,
      Version: strictVersion + 2,
    })
  })

  it('checks the annotation of an annotation or descendant-type schema, and the pair first', async () => {
    const key = 'exe.northwind.invoicing.prod'
    expect(await restoreAnnotationSchemaVersion(org, project, view, key, 1)).toMatchObject({
      Outcome: 'Restored',
      Version: 3,
    })
    expect(await restoreDescendantTypeSchemaVersion(org, project, view, 'sbt.northwind', 'exe', 1)).toMatchObject({
      Outcome: 'Unchanged',
      Version: 1,
    })
    await expect(
      restoreDescendantTypeSchemaVersion(org, project, view, key, 'rst', 1, { force: true }),
    ).rejects.toMatchObject({ status: 400, errorCode: 'InvalidKey' })

    await deleteAnnotation(org, project, view, key)
    await expect(restoreAnnotationSchemaVersion(org, project, view, key, 2)).rejects.toMatchObject({
      status: 409,
      errorCode: 'AnnotationDeleted',
    })
  })
})

describe('VersionTimeline', () => {
  const versions: ConfigurationVersion[] = [
    { Version: 1, Modified: stamp, ExpirationTime: '2020-01-01T00:00:00Z' },
    { Version: 2, Modified: stamp },
    { Version: 3, Modified: stamp, RestoredFrom: 2 },
  ]
  const restoreButtons = (screen: Awaited<ReturnType<typeof mountScreen>>) =>
    screen.host.findAll('button').filter((button) => button.text() === 'Restore')

  it('offers Restore on a version that is neither current nor expired, and names a restored one', async () => {
    const screen = await mountScreen(VersionTimeline, { versions, current: 3, restorable: true })
    await settle()

    expect(restoreButtons(screen).map((button) => button.attributes('aria-label'))).toEqual(['Restore v2'])
    expect(screen.host.text()).toContain('restored from v2')
    await restoreButtons(screen)[0]!.trigger('click')
    expect(screen.inner().emitted('restore')).toEqual([[2]])
    screen.host.unmount()
  })

  it('offers no Restore when the history cannot restore', async () => {
    const screen = await mountScreen(VersionTimeline, { versions, current: 3 })
    await settle()

    expect(restoreButtons(screen)).toEqual([])
    screen.host.unmount()
  })
})

describe('the Restore of the history', () => {
  beforeEach(clearToasts)

  const page = () => document.body.textContent ?? ''
  const dialogButton = (label: string) =>
    [...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find(
      (button) => button.textContent?.trim() === label,
    )

  function fakeSource(restore: VersionSource['restore']) {
    const afterRestore = vi.fn(() => Promise.resolve())
    const source: VersionSource = {
      document: 'configuration',
      queryKey: ['v1', org, project, view, 'configuration', 'exe.a.b.c'],
      list: () =>
        Promise.resolve([
          { Version: 1, Modified: stamp },
          { Version: 2, Modified: stamp },
          { Version: 3, Modified: stamp },
        ]),
      content: () => Promise.resolve({}),
      diff: () => Promise.resolve({}),
      current: 3,
      restore,
      afterRestore,
    }
    return { source, afterRestore }
  }

  async function mountView(source: VersionSource, accessMap?: Record<string, string>) {
    const screen = await mountScreen(VersionsView, { source }, { path: '/acme/billing/default/versions', accessMap })
    await settle(6)
    return screen
  }

  async function confirmRestore(screen: Awaited<ReturnType<typeof mountView>>, version: number) {
    await screen.host.find(`button[aria-label="Restore v${version}"]`).trigger('click')
    await settle()
    expect(document.body.querySelector('[role="dialog"]')?.textContent).toContain(`Restore v${version}?`)
    dialogButton(`Restore v${version}`)!.click()
    await settle(6)
  }

  it('restores a version after its confirmation, and refreshes what it changed', async () => {
    const restore = vi.fn((version: number) =>
      Promise.resolve<VersionRestoreResult>({ Outcome: 'Restored', FromVersion: version, Version: 4, Modified: stamp }),
    )
    const { source, afterRestore } = fakeSource(restore)
    const screen = await mountView(source)

    await confirmRestore(screen, 2)

    expect(restore).toHaveBeenCalledWith(2, false)
    expect(page()).toContain('Restored v2 as v4')
    expect(afterRestore).toHaveBeenCalledWith(expect.objectContaining({ Outcome: 'Restored', Version: 4 }))
    screen.host.unmount()
  })

  it('restores nothing when the confirmation is cancelled', async () => {
    const restore = vi.fn()
    const screen = await mountView(fakeSource(restore).source)

    await screen.host.find('button[aria-label="Restore v1"]').trigger('click')
    await settle()
    dialogButton('Cancel')!.click()
    await settle(6)

    expect(restore).not.toHaveBeenCalled()
    screen.host.unmount()
  })

  it('says that nothing changed for the current version or one with its content', async () => {
    const restore = vi.fn((version: number) =>
      Promise.resolve<VersionRestoreResult>({
        Outcome: 'Unchanged',
        FromVersion: version,
        Version: version === 2 ? 2 : 3,
        Modified: stamp,
      }),
    )
    const screen = await mountView(fakeSource(restore).source)

    await confirmRestore(screen, 2)
    expect(page()).toContain('Nothing changed')
    expect(page()).toContain('v2 is the current version.')

    await confirmRestore(screen, 1)
    expect(page()).toContain('The current version v3 already has the content of v1.')
    screen.host.unmount()
  })

  it('offers Restore anyway when the version fails its schema, and restores it with force', async () => {
    const refusal = new StorytellerError({
      status: 409,
      errorCode: 'SchemaValidationFailed',
      message: 'The configuration of exe.a.b.c does not comply with its schema.',
      schemaErrors: [{ AnnotationKey: 'exe.a.b.c', ViewName: 'default', Errors: ['#/retries: NumberTooBig'] }],
    })
    const restore = vi.fn((version: number, force?: boolean) =>
      force
        ? Promise.resolve<VersionRestoreResult>({
            Outcome: 'Restored',
            FromVersion: version,
            Version: 4,
            Modified: stamp,
          })
        : Promise.reject(refusal),
    )
    const screen = await mountView(fakeSource(restore).source)

    await confirmRestore(screen, 2)
    expect(screen.host.text()).toContain('v2 does not match the schema')
    expect(screen.host.text()).toContain('/retries Number too big')
    expect(page()).not.toContain('Restored v2')

    await screen.host
      .findAll('button')
      .find((button) => button.text() === 'Restore anyway')!
      .trigger('click')
    await settle()
    expect(document.body.querySelector('[role="dialog"]')?.textContent).toContain('Restore although the schema fails?')
    dialogButton('Restore anyway')!.click()
    await settle(6)

    expect(restore).toHaveBeenLastCalledWith(2, true)
    expect(page()).toContain('Restored v2 as v4')
    expect(screen.host.text()).not.toContain('does not match the schema')
    screen.host.unmount()
  })

  it('shows the error of any other refusal', async () => {
    const restore = vi.fn(() =>
      Promise.reject(
        new StorytellerError({ status: 404, errorCode: 'VersionNotFound', message: 'Version 2 has expired.' }),
      ),
    )
    const screen = await mountView(fakeSource(restore).source)

    await confirmRestore(screen, 2)

    expect(page()).toContain('v2 was not restored')
    expect(page()).toContain('Version 2 has expired.')
    screen.host.unmount()
  })

  it('says that a restored version was not refreshed, never that it was not restored', async () => {
    const restore = vi.fn((version: number) =>
      Promise.resolve<VersionRestoreResult>({ Outcome: 'Restored', FromVersion: version, Version: 4, Modified: stamp }),
    )
    const { source } = fakeSource(restore)
    source.afterRestore = () => Promise.reject(new Error('The network is down.'))
    const screen = await mountView(source)

    await confirmRestore(screen, 2)

    expect(page()).toContain('Restored v2 as v4')
    expect(page()).toContain('The views were not refreshed')
    expect(page()).not.toContain('v2 was not restored')
    screen.host.unmount()
  })

  it('warns in the confirmation that unsaved changes of the editor are discarded', async () => {
    const restore = vi.fn()
    const { source } = fakeSource(restore)
    source.unsavedChanges = true
    const screen = await mountView(source)

    await screen.host.find('button[aria-label="Restore v1"]').trigger('click')
    await settle()

    expect(document.body.querySelector('[role="dialog"]')?.textContent).toContain(
      'Your unsaved changes in the editor are discarded.',
    )
    dialogButton('Cancel')!.click()
    await settle(6)
    screen.host.unmount()
  })

  it('offers no Restore to a Reader', async () => {
    const screen = await mountView(fakeSource(vi.fn()).source, { 'acme.billing': 'Reader' })

    expect(screen.host.findAll('button').some((button) => button.text() === 'Restore')).toBe(false)
    expect(screen.host.text()).toContain('v1')
    screen.host.unmount()
  })
})

describe('the versions of a deleted document', () => {
  beforeEach(clearToasts)

  it('are listed for a deleted configuration, and a restore brings it back', async () => {
    const { key, current } = configuredExecution()
    await deleteConfiguration(org, project, view, key)
    const screen = await mountScreen(
      ConfigurationVersions,
      { annotationKey: key },
      { path: `/acme/billing/default/configurations/${key}/versions` },
    )
    await settle(10)

    expect(screen.host.text()).toContain('The configuration is deleted')
    expect(screen.host.text()).toContain('Restore one of its versions to bring it back.')
    expect(screen.host.text()).not.toContain('No configuration, no history')

    await screen.host.find(`button[aria-label="Restore v${current}"]`).trigger('click')
    await settle()
    ;[...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')]
      .find((button) => button.textContent?.trim() === `Restore v${current}`)!
      .click()
    await settle(10)

    expect(document.body.textContent).toContain(`Restored v${current} as v${current + 1}`)
    expect(ownContent(loadDb(), scope, key)).toEqual({ retries: 5 })
    await vi.waitFor(() => expect(screen.host.text()).not.toContain('The configuration is deleted'))
    screen.host.unmount()
  })

  it('are hidden while the annotation is deleted: it has to be restored first, no Retry', async () => {
    const { key } = configuredExecution()
    await deleteAnnotation(org, project, view, key)
    const screen = await mountScreen(
      ConfigurationVersions,
      { annotationKey: key },
      { path: `/acme/billing/default/configurations/${key}/versions?v=2` },
    )
    await settle(10)

    const text = screen.host.text()
    expect(text).toContain('The annotation is deleted')
    expect(text).toContain('Restore the annotation before its configuration versions can be seen or restored.')
    expect(screen.host.find(`a[href="/acme/billing/default/annotations/${key}"]`).text()).toBe('Open the annotation')
    expect(text).not.toContain('Retry')
    expect(text).not.toContain('No configuration, no history')
    expect(screen.host.find('button[aria-label^="Restore v"]').exists()).toBe(false)
    screen.host.unmount()
  })

  it('are hidden also when only the version list answers that the annotation is deleted', async () => {
    const { key } = configuredExecution()
    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key/versions', () =>
        HttpResponse.json(
          {
            Message: `The annotation '${key}' is deleted; restore it to see its configuration.`,
            ErrorCode: 'AnnotationDeleted',
          },
          { status: 404 },
        ),
      ),
    )
    const screen = await mountScreen(
      ConfigurationVersions,
      { annotationKey: key },
      { path: `/acme/billing/default/configurations/${key}/versions` },
    )
    await settle(10)

    expect(screen.host.text()).toContain('The annotation is deleted')
    expect(screen.host.text()).not.toContain('Retry')
    screen.host.unmount()
  })

  it('reports a failed refresh of a restored template and keeps its editor as it was', async () => {
    const screen = await mountScreen(
      TemplateDetail,
      { type: 'exe' },
      { path: '/acme/billing/default/modeling/templates/exe?tab=versions' },
    )
    await settle(10)
    server.use(
      http.get('*/v1/:org/:project/:view/template/:type', () =>
        HttpResponse.json({ Message: 'The service is down.' }, { status: 500 }),
      ),
    )

    await screen.host.find('button[aria-label="Restore v1"]').trigger('click')
    await settle()
    ;[...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')]
      .find((button) => button.textContent?.trim() === 'Restore v1')!
      .click()
    await settle(10)

    const text = document.body.textContent ?? ''
    expect(text).toContain('The views were not refreshed')
    expect(text).not.toContain('v1 was not restored')
    screen.host.unmount()
  })

  it('are listed for a deleted template, and a type without one shows no history', async () => {
    await deleteTemplate(org, project, view, 'exe')
    const deleted = await mountScreen(
      TemplateDetail,
      { type: 'exe' },
      { path: '/acme/billing/default/modeling/templates/exe?tab=versions' },
    )
    await settle(10)

    expect(deleted.host.text()).toContain('The template is deleted')
    expect(deleted.host.find('button[aria-label="Restore v3"]').exists()).toBe(true)
    expect(deleted.host.text()).not.toContain('No template, no history')
    deleted.host.unmount()

    const none = await mountScreen(
      TemplateDetail,
      { type: 'rst' },
      { path: '/acme/billing/default/modeling/templates/rst?tab=versions' },
    )
    await settle(10)
    expect(none.host.text()).toContain('No template, no history')
    none.host.unmount()
  })
})
