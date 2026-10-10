import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { getAllAncestors } from '@/utils/annotation-key'
import { schemaApi, schemaRoute, summaryTarget } from '@/utils/schema-targets'
import { validateSchemaDocument } from '@/utils/schema-validation'
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
