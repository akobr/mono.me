import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import {
  deleteAnnotation,
  findDeletedAnnotations,
  getAnnotation,
  getAnnotationHistory,
  getDeletedAnnotations,
  purgeAnnotation,
  restoreAnnotations,
  setAnnotation,
  setConfiguration,
  type Annotation,
  type AnnotationAuditEntry,
  type ConfigurationSummary,
} from '@42for.net/2splatform.sdk'
import AnnotationHistory from '@/components/annotations/AnnotationHistory.vue'
import ConfigurationLink from '@/components/annotations/ConfigurationLink.vue'
import DeletedKeysDialog from '@/components/annotations/DeletedKeysDialog.vue'
import { useDeletedKeyCheck, type DeletedKeyCheck } from '@/composables/useDeletedKeyCheck'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'
import { useCatalogStore } from '@/stores/catalog'
import { actorName, auditChanges, causeText } from '@/utils/audit'
import { configurationOrigin, describeInheritance } from '@/utils/configuration-origin'
import { annotationFormSchema, invalidAnnotationFields } from '@/forms/annotation.schema'
import { mountScreen, settle } from '../../helpers'

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const [org, project, view] = ['acme', 'billing', 'default']

const summary = (key: string, version = 1, hasContent = true): [string, ConfigurationSummary] => [
  key,
  { AnnotationKey: key, Version: version, HasContent: hasContent },
]

describe('configurationOrigin', () => {
  const key = 'exe.northwind.invoicing.prod'

  it('is unknown while the summaries load and none without anything to inherit', () => {
    expect(configurationOrigin(key, null, undefined)).toEqual({ kind: 'unknown' })
    expect(configurationOrigin(key, new Map(), new Set())).toEqual({ kind: 'none' })
    // a stored entity without content (a cached calculation) is not own content
    expect(configurationOrigin(key, new Map([summary(key, 3, false)]), new Set())).toEqual({ kind: 'none' })
  })

  it('knows own content with its version and what else merges into it', () => {
    const origin = configurationOrigin(
      key,
      new Map([summary(key, 7), summary('rst.invoicing'), summary('sbt.contoso')]),
      new Set(['usg']),
    )
    expect(origin).toEqual({ kind: 'own', version: 7, inheritsFrom: ['rst.invoicing'], templates: ['usg'] })
    expect(describeInheritance(origin as never)).toBe('rst.invoicing and the Usage template')
  })

  it('is inherited from ancestors in merge order, or only from templates', () => {
    expect(
      configurationOrigin(key, new Map([summary('cnt.northwind.prod'), summary('rst.invoicing')]), new Set()),
    ).toEqual({ kind: 'inherited', inheritsFrom: ['rst.invoicing', 'cnt.northwind.prod'], templates: [] })
    expect(configurationOrigin(key, new Map(), new Set(['exe', 'rst']))).toEqual({
      kind: 'inherited',
      inheritsFrom: [],
      templates: ['rst', 'exe'],
    })
  })
})

describe('audit presentation', () => {
  const entry = (extra: Partial<AnnotationAuditEntry>): AnnotationAuditEntry => ({ Action: 'Updated', ...extra })

  it('lists fields with both values, flags the description and shows labels, values and versions', () => {
    const changes = auditChanges(
      entry({
        Fields: [
          { Field: 'Title', Previous: 'Old' as never, Value: 'Very long' as never, IsValueTruncated: true },
          { Field: 'Description' },
        ],
        Labels: { Added: ['eu'], Removed: ['us'] },
        Values: [
          {
            Key: 'plan',
            Change: 'Changed',
            PreviousKind: 'String',
            Previous: 'pro' as never,
            ValueKind: 'String',
            Value: 'ent' as never,
          },
          { Key: 'settings', Change: 'Changed', PreviousKind: 'Object', ValueKind: 'Object' },
          { Key: 'tier', Change: 'Added', ValueKind: 'Array' },
          { Key: 'legacy', Change: 'Removed', PreviousKind: 'Boolean', Previous: true as never },
        ],
        ConfigurationVersion: 7,
      }),
    )

    expect(changes).toEqual([
      { kind: 'field', field: 'Title', previous: '"Old"', value: '"Very long…"' },
      { kind: 'flag', field: 'Description' },
      { kind: 'labels', added: ['eu'], removed: ['us'] },
      { kind: 'value', key: 'plan', text: 'plan: "pro" → "ent"' },
      { kind: 'value', key: 'settings', text: 'settings: object changed' },
      { kind: 'value', key: 'tier', text: 'tier added: array' },
      { kind: 'value', key: 'legacy', text: 'legacy removed' },
      { kind: 'configuration', version: 7 },
    ])
  })

  it('names actors and causes', () => {
    expect(actorName({ Kind: 'Account', Id: 'user_1', Name: 'Ada' })).toBe('Ada')
    expect(actorName({ Kind: 'Account', Id: 'user_1' })).toBe('user_1')
    expect(actorName({ Kind: 'Machine', Id: 'mach-1' })).toBe('machine:mach-1')
    expect(actorName({ Kind: 'System' })).toBe('System')
    expect(causeText({ Kind: 'Cascade', Key: 'sbt.northwind' })).toBe('with sbt.northwind')
    expect(causeText({ Kind: 'Ancestor', Key: 'exe.a.b.c' })).toBe('for exe.a.b.c')
  })
})

describe('annotation limits', () => {
  it('rejects what the API rejects and maps a 400 InvalidAnnotation to its fields', () => {
    const draft = {
      ProjectName: 'billing',
      ViewName: 'default',
      AnnotationKey: 'sbt.a',
      Name: 'a',
      AnnotationType: 'Subject',
    }
    const result = annotationFormSchema.safeParse({
      ...draft,
      Description: 'x'.repeat(10_001),
      DocumentationLink: `https://example.com/${'x'.repeat(2048)}`,
    })
    expect(result.success).toBe(false)
    expect(result.error!.issues.map((issue) => [issue.path.join('.'), issue.message])).toEqual(
      expect.arrayContaining([
        ['Description', 'At most 10,000 characters.'],
        ['DocumentationLink', 'At most 2048 characters.'],
      ]),
    )

    expect(
      invalidAnnotationFields(
        "The annotation 'sbt.a' is not valid: Title has 201 characters, at most 200 are allowed. DocumentationLink must be an absolute http or https URL.",
      ),
    ).toEqual({
      Title: 'Title has 201 characters, at most 200 are allowed.',
      DocumentationLink: 'DocumentationLink must be an absolute http or https URL.',
    })
    expect(invalidAnnotationFields('Title has 201 characters, at most 200 are allowed.')).toEqual({
      Title: 'Title has 201 characters, at most 200 are allowed.',
    })
  })
})

describe('the mock API lifecycle', () => {
  it('soft-deletes the tree, lists the roots, restores and purges like the API', async () => {
    const deletion = await deleteAnnotation(org, project, view, 'sbt.northwind')
    expect(deletion.DeletedKeys!.at(-1)).toBe('sbt.northwind')
    expect(deletion.DeletedKeys).toContain('cnt.northwind.prod')
    expect(deletion.Deletion).toMatchObject({ RootKey: 'sbt.northwind', IsRoot: true })
    await expect(getAnnotation(org, project, view, 'cnt.northwind.prod')).rejects.toMatchObject({ status: 404 })

    const roots = await getDeletedAnnotations(org, project, view, { roots: true })
    expect(roots.Annotations!.map((annotation) => annotation.AnnotationKey)).toEqual(['sbt.northwind'])
    const lookup = await findDeletedAnnotations(org, project, view, {
      Keys: ['exe.northwind.invoicing.prod', 'sbt.contoso'],
    })
    expect(lookup.map((annotation) => annotation.AnnotationKey)).toEqual(['exe.northwind.invoicing.prod'])

    // restored alone, the subject leaves its deleted children deleted, as roots
    const restored = await restoreAnnotations(org, project, view, { Keys: ['sbt.northwind'], Descendants: false })
    expect(restored.Annotations!.map((annotation) => annotation.AnnotationKey)).toEqual(['sbt.northwind'])
    const after = await getDeletedAnnotations(org, project, view, { roots: true })
    expect(after.Annotations!.map((annotation) => annotation.AnnotationKey)).toContain('cnt.northwind.prod')

    // a permanent delete needs an Administrator
    const db = loadDb()
    db.account!.AccessMap!['acme.billing'] = 'Contributor'
    saveDb(db)
    await expect(purgeAnnotation(org, project, view, 'cnt.northwind.prod')).rejects.toMatchObject({ status: 403 })
    db.account!.AccessMap!['acme.billing'] = 'Owner'
    saveDb(db)
    await expect(purgeAnnotation(org, project, view, 'sbt.northwind')).rejects.toMatchObject({
      status: 409,
      errorCode: 'AnnotationNotDeleted',
    })
    expect((await purgeAnnotation(org, project, view, 'cnt.northwind.prod')).PurgedKeys).toContain('cnt.northwind.prod')
    await expect(getAnnotationHistory(org, project, view, 'cnt.northwind.prod')).rejects.toMatchObject({ status: 404 })
  })
})

const Harness = defineComponent({
  props: { keys: { type: Array, required: true } },
  emits: ['done'],
  setup(props, { emit, expose }) {
    const check = useDeletedKeyCheck()
    expose({ run: () => check.check(props.keys as string[]).then((result) => emit('done', result)) })
    return () =>
      h(DeletedKeysDialog, {
        open: check.open.value,
        'onUpdate:open': (value: boolean) => (check.open.value = value),
        deleted: check.deleted.value,
        onChoose: check.choose,
      })
  },
})

async function runCheck(key: string, answer?: 'Restore and update' | 'Create new' | 'Cancel') {
  const results: DeletedKeyCheck[] = []
  const screen = await mountScreen(Harness, { keys: [key], onDone: (result: DeletedKeyCheck) => results.push(result) })
  void (screen.inner().vm as unknown as { run: () => Promise<void> }).run()
  await settle(10)
  const dialog = document.body.querySelector('[role="dialog"]')
  if (answer) {
    const button = [...document.body.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === answer,
    )
    button!.click()
    await settle(6)
  }
  screen.host.unmount()
  return { result: results[0], dialog }
}

describe('useDeletedKeyCheck and DeletedKeysDialog', () => {
  it('answers none without asking when nothing is deleted', async () => {
    const { result, dialog } = await runCheck('exe.northwind.invoicing.prod')
    expect(result).toEqual({ choice: 'none', deleted: [] })
    expect(dialog).toBeNull()
  })

  it('asks when the key itself is deleted and returns the choice', async () => {
    await deleteAnnotation(org, project, view, 'exe.northwind.invoicing.prod')
    const { result, dialog } = await runCheck('exe.northwind.invoicing.prod', 'Restore and update')
    expect(dialog?.textContent).toContain('These annotations were deleted')
    expect(result!.choice).toBe('restore')
    expect(result!.deleted.map((annotation: Annotation) => annotation.AnnotationKey)).toEqual([
      'exe.northwind.invoicing.prod',
    ])
  })

  it('asks when only an ancestor is deleted, and Create new and Cancel are answers too', async () => {
    await deleteAnnotation(org, project, view, 'rst.invoicing')
    const created = await runCheck('exe.contoso.invoicing.newctx', 'Create new')
    expect(created.result!.choice).toBe('create')
    // the responsibility took the usage of contoso with it: both are on the way to the new key
    expect(created.result!.deleted.map((annotation: Annotation) => annotation.AnnotationKey).toSorted()).toEqual([
      'rst.invoicing',
      'usg.contoso.invoicing',
    ])

    const cancelled = await runCheck('exe.contoso.invoicing.newctx', 'Cancel')
    expect(cancelled.result!.choice).toBe('cancel')
  })
})

describe('AnnotationHistory', () => {
  it('renders every action with its actor, cause and changes', async () => {
    const key = 'usg.northwind.invoicing'
    const current = await getAnnotation(org, project, view, key)
    await setAnnotation(org, project, view, key, { ...current, Title: 'Billing of Northwind', Labels: ['eu'] })
    await setConfiguration(org, project, view, key, { retries: 3 })
    await deleteAnnotation(org, project, view, 'sbt.northwind')
    await restoreAnnotations(org, project, view, { Keys: ['sbt.northwind'] })
    await deleteAnnotation(org, project, view, key)
    await setAnnotation(org, project, view, key, { ...current, Title: 'New lifetime' })

    const screen = await mountScreen(AnnotationHistory, { annotationKey: key })
    await settle(10)
    const text = screen.host.text()

    for (const label of ['Created', 'Updated', 'Configuration saved', 'Deleted', 'Restored', 'Re-created']) {
      expect(text).toContain(label)
    }
    expect(text).toMatch(/Title: ".+" → "Billing of Northwind"/)
    expect(text).toContain('with sbt.northwind')
    expect(text).toContain('+eu')
    expect(text).toMatch(/Configuration saved as v\d+/)
    expect(text).toContain('Ada Lovelace')
    expect(text).toContain('System')
    screen.host.unmount()
  })

  it('shows a deleted annotation with its banner', async () => {
    await deleteAnnotation(org, project, view, 'sbt.contoso')
    const screen = await mountScreen(AnnotationHistory, { annotationKey: 'sbt.contoso' })
    await settle(10)
    expect(screen.host.find('[data-testid="deleted-banner"]').text()).toMatch(
      /Deleted on .+ by Ada Lovelace with \d+ descendants/,
    )
    expect(screen.host.text()).toContain('Restore')
    screen.host.unmount()
  })
})

describe('ConfigurationLink', () => {
  it('is solid for own content, outline when inherited and ghost without anything', async () => {
    server.use(http.get('*/api/v1/:org/:project/:view/templates', () => HttpResponse.json([])))

    const render = async (key: string) => {
      const screen = await mountScreen(ConfigurationLink, { annotationKey: key })
      useCatalogStore().configurations = new Map([summary('exe.northwind.invoicing.prod', 4), summary('rst.invoicing')])
      await settle(6)
      const link = screen.host.find('[data-origin]')
      const result = { origin: link.attributes('data-origin'), text: link.text(), href: link.attributes('href') }
      screen.host.unmount()
      return result
    }

    expect(await render('exe.northwind.invoicing.prod')).toMatchObject({
      origin: 'own',
      text: 'Configuration · v4',
      href: '/acme/billing/default/configurations/exe.northwind.invoicing.prod',
    })
    expect(await render('usg.northwind.invoicing')).toMatchObject({
      origin: 'inherited',
      text: 'Inherited configuration',
    })
    expect(await render('sbt.contoso')).toMatchObject({ origin: 'none', text: 'No configuration' })
  })
})

describe('the removed detail tabs', () => {
  it('redirect to the configuration detail, so saved links keep working', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes })
    const base = '/acme/billing/default'

    await router.push(`${base}/annotations/exe.northwind.invoicing.prod/versions?v=3`)
    expect(router.currentRoute.value.fullPath).toBe(`${base}/configurations/exe.northwind.invoicing.prod/versions?v=3`)

    await router.push(`${base}/graph/exe.northwind.invoicing.prod/configuration?cfg=resolved`)
    expect(router.currentRoute.value.path).toBe(`${base}/configurations/exe.northwind.invoicing.prod/resolved`)

    await router.push(`${base}/annotations/exe.northwind.invoicing.prod/schema`)
    expect(router.currentRoute.value.path).toBe(`${base}/configurations/exe.northwind.invoicing.prod/schema`)
  })
})
