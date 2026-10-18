import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { VueWrapper } from '@vue/test-utils'
import { setupServer } from 'msw/node'
import {
  getDescendantTypeSchema,
  getDescendantTypeSchemaVersions,
  setDescendantTypeSchema,
} from '@42for.net/2splatform.sdk'
import { getAllAncestors } from '@/utils/annotation-key'
import { schemaApi, schemaRoute, summaryTarget } from '@/utils/schema-targets'
import { validateSchemaDocument } from '@/utils/schema-validation'
import SchemaCreateDialog from '@/components/schemas/SchemaCreateDialog.vue'
import SchemaDetail from '@/components/schemas/SchemaDetail.vue'
import TemplateDetail from '@/components/templates/TemplateDetail.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb } from '@/mock/db'
import { getDocument, latest } from '@/mock/documents'
import { mountScreen, settle } from '../../helpers'

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }

describe('schema targets', () => {
  it('builds routes and API paths per kind', () => {
    expect(schemaRoute(scope, { kind: 'type', type: 'exe' })).toBe('/acme/billing/default/schemas/type/exe')
    expect(schemaRoute(scope, { kind: 'descendant', key: 'sbt.a', type: 'exe' })).toBe(
      '/acme/billing/default/schemas/annotation/sbt.a/type/exe',
    )
    expect(schemaApi(scope, { kind: 'annotation', key: 'exe.a.b.c' }).path).toBe(
      '/v1/acme/billing/default/configuration-schema/exe.a.b.c',
    )
  })

  it('maps listing rows (companion spec E7) to targets', () => {
    expect(summaryTarget({ Kind: 'Type', AnnotationType: 'exe' })).toEqual({ kind: 'type', type: 'exe' })
    expect(summaryTarget({ Kind: 'Annotation', AnnotationKey: 'exe.a.b.c' })).toEqual({
      kind: 'annotation',
      key: 'exe.a.b.c',
    })
    expect(summaryTarget({ Kind: 'DescendantType', AnnotationKey: 'sbt.a', AnnotationType: 'Execution' })).toEqual({
      kind: 'descendant',
      key: 'sbt.a',
      type: 'exe',
    })
    expect(summaryTarget({ Kind: 'Type' })).toBeUndefined()
  })

  it('knows which configurations a schema applies to', () => {
    const below = schemaApi(scope, { kind: 'descendant', key: 'sbt.a', type: 'exe' })

    expect(below.appliesTo('exe.a.r.c', getAllAncestors('exe.a.r.c'))).toBe(true)
    expect(below.appliesTo('exe.b.r.c', getAllAncestors('exe.b.r.c'))).toBe(false)
    expect(below.appliesTo('cnt.a.c', getAllAncestors('cnt.a.c'))).toBe(false)
  })
})

describe('validateSchemaDocument', () => {
  it('checks a schema against its meta-schema', () => {
    expect(validateSchemaDocument({ type: 'object', properties: { a: { type: 'string' } } })).toEqual([])
    expect(validateSchemaDocument({ type: 'objekt' })).not.toEqual([])
    expect(
      validateSchemaDocument({ $schema: 'https://json-schema.org/draft/2020-12/schema', prefixItems: [{}] }),
    ).toEqual([])
    expect(validateSchemaDocument([])).toEqual([
      { pointer: '', message: 'A schema must be an object.', keyword: 'type' },
    ])
  })
})

describe('SchemaDetail', () => {
  it('lists the configurations a schema change would break, with Save anyway', async () => {
    const view = await mountScreen(
      SchemaDetail,
      { target: { kind: 'type', type: 'exe' } },
      { path: '/acme/billing/default/schemas/type/exe' },
    )
    await settle(10)

    const schema = JSON.parse(view.host.find('textarea').element.value) as { properties: Record<string, unknown> }
    schema.properties.retries = { type: 'integer', maximum: 0 }
    await view.host.find('textarea').setValue(JSON.stringify(schema, null, 2))
    await settle()
    await view.host
      .findAll('button')
      .find((button) => button.text() === 'Save')!
      .trigger('click')
    await settle(10)

    expect(view.host.text()).toContain('Existing configurations would not comply')
    expect(view.host.text()).toContain('/retries Number too big')
    expect(view.host.text()).toContain('Save anyway')
    expect(latest(getDocument(loadDb(), scope, 'schema:type:exe'))?.Version).toBe(3)
    view.host.unmount()
  })

  it('reports meta-schema errors and blocks saving', async () => {
    const view = await mountScreen(
      SchemaDetail,
      { target: { kind: 'type', type: 'cnt' } },
      { path: '/acme/billing/default/schemas/type/cnt' },
    )
    await settle(10)

    await view.host.find('textarea').setValue('{ "type": "objekt" }')
    await settle()

    expect(view.host.text()).toMatch(/must be equal to one of the allowed values|must match/)
    expect(
      view.host
        .findAll('button')
        .find((button) => button.text() === 'Save')!
        .attributes('disabled'),
    ).toBeDefined()
    view.host.unmount()
  })
})

describe('SchemaCreateDialog', () => {
  // A failed test must not leave its teleported dialog in the body for the next one.
  let unmount: (() => void) | undefined
  afterEach(() => {
    unmount?.()
    unmount = undefined
  })

  type Field = 'RadioGroup' | 'Select' | 'InputMenu'
  type SelectProps = { items: { value: string }[]; modelValue?: string; disabled?: boolean }

  /** Opens the dialog on a descendant-type schema; `choose` picks a value in a field as the user would. */
  async function openDialog() {
    const screen = await mountScreen(SchemaCreateDialog, { open: true })
    unmount = () => screen.host.unmount()
    await settle(6)
    const field = (name: Field) => screen.inner().findComponent({ name }) as VueWrapper
    const choose = async (name: Field, value: string) => {
      field(name).vm.$emit('update:modelValue', value)
      await settle()
    }
    const select = () => field('Select').props() as SelectProps
    await choose('RadioGroup', 'descendant')
    return { screen, choose, select }
  }

  // The modal is teleported to the body.
  const text = () => document.body.querySelector<HTMLElement>('[role="dialog"]')!.textContent!.replace(/\s+/g, ' ')
  const openEditor = () =>
    [...document.body.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Open editor')!
  const offered = (select: SelectProps) => select.items.map((item) => item.value)

  it('offers only the types that can descend from the key', async () => {
    const { choose, select } = await openDialog()
    // no key yet, so any type
    expect(offered(select())).toEqual(['rst', 'unt', 'sbt', 'usg', 'cnt', 'exe', 'uxe'])

    await choose('InputMenu', 'rst.payments')
    expect(offered(select())).toEqual(['unt', 'usg', 'exe', 'uxe'])
    expect(text()).not.toContain('No annotation type descends')

    await choose('InputMenu', 'exe.northwind.invoicing.prod')
    expect(offered(select())).toEqual(['uxe'])

    await choose('InputMenu', 'uxe.northwind.invoicing.prod.worker')
    expect(offered(select())).toEqual([])
    expect(select().disabled).toBe(true)
    expect(text()).toContain('No annotation type descends from uxe.northwind.invoicing.prod.worker.')
  })

  it('selects the first type that can descend when the key changes', async () => {
    const { choose, select, screen } = await openDialog()
    await choose('InputMenu', 'rst.payments')
    expect(select().modelValue).toBe('exe')

    await choose('Select', 'unt')
    await choose('InputMenu', 'sbt.northwind')
    expect(select().modelValue).toBe('usg')

    await choose('InputMenu', 'cnt.northwind.prod')
    expect(select().modelValue).toBe('exe')

    await choose('InputMenu', 'unt.invoicing.worker')
    expect(select().modelValue).toBe('uxe')

    // a type that still descends stays selected
    await choose('InputMenu', 'rst.payments')
    expect(select().modelValue).toBe('uxe')

    openEditor().click()
    await settle()
    expect(screen.router.currentRoute.value.path).toBe('/acme/billing/default/schemas/annotation/rst.payments/type/uxe')
  })

  it('keeps Open editor disabled for a unit of execution key', async () => {
    const { choose, select } = await openDialog()
    await choose('InputMenu', 'uxe.northwind.invoicing.prod.worker')
    expect(openEditor().disabled).toBe(true)

    // its annotation schema still exists
    await choose('RadioGroup', 'annotation')
    expect(openEditor().disabled).toBe(false)

    // and a type schema takes any type again
    await choose('RadioGroup', 'type')
    expect(offered(select())).toHaveLength(7)
    expect(select().modelValue).toBe('rst')
    expect(openEditor().disabled).toBe(false)
  })
})

describe('descendant-type schemas of the mock API', () => {
  const schema = { type: 'object' }
  const message =
    'The type rst cannot descend from exe.northwind.invoicing.prod, so it has no descendant-type schema there.'

  it('rejects a type that cannot descend from the key, also with force, and stores nothing', async () => {
    const key = 'exe.northwind.invoicing.prod'
    for (const [type, params] of [
      ['rst', undefined],
      ['rst', { force: true }],
      ['RST', { force: true }],
    ] as const) {
      await expect(
        setDescendantTypeSchema(scope.org, scope.project, scope.view, key, type, schema, params),
      ).rejects.toMatchObject({ status: 400, errorCode: 'InvalidKey', message })
    }

    await expect(getDescendantTypeSchema(scope.org, scope.project, scope.view, key, 'rst')).rejects.toMatchObject({
      status: 404,
    })
    expect(await getDescendantTypeSchemaVersions(scope.org, scope.project, scope.view, key, 'rst')).toEqual([])
    expect(getDocument(loadDb(), scope, `schema:descendant:${key}:rst`)).toBeUndefined()
  })

  it('stores a type that descends from the key', async () => {
    const saved = await setDescendantTypeSchema(scope.org, scope.project, scope.view, 'rst.payments', 'exe', schema)

    expect(saved).toMatchObject({ AnnotationKey: 'rst.payments', AnnotationType: 'exe', Content: schema })
    const read = await getDescendantTypeSchema(scope.org, scope.project, scope.view, 'rst.payments', 'exe')
    expect(read.Content).toEqual(schema)
  })
})

describe('TemplateDetail', () => {
  it('saves template edits as a guarded JSON Patch', async () => {
    const view = await mountScreen(TemplateDetail, { type: 'exe' }, { path: '/acme/billing/default/templates/exe' })
    await settle(10)

    const template = JSON.parse(view.host.find('textarea').element.value) as Record<string, unknown>
    await view.host.find('textarea').setValue(JSON.stringify({ ...template, retries: 6 }, null, 2))
    await settle()
    await view.host
      .findAll('button')
      .find((button) => button.text() === 'Save')!
      .trigger('click')
    await settle(10)

    const stored = latest(getDocument(loadDb(), scope, 'template:exe'))
    expect(stored?.Version).toBe(4)
    expect(stored?.Content?.retries).toBe(6)
    view.host.unmount()
  })
})
