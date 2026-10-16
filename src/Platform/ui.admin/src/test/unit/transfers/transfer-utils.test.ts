import { describe, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/vue-query'
import type { ViewImport, ViewImportItem } from '@42for.net/2splatform.sdk'
import { invalidateImport } from '@/queries/invalidation'
import {
  contentOf,
  documentExists,
  documentName,
  documentPath,
  exportCounts,
  exportImportPath,
  joinList,
  recreatedAnnotations,
  writtenConfigurations,
  writtenContents,
} from '@/utils/transfers'

// The result and the cache refresh of an import (spec 4.4 and 4.5 of the Export and Import of a View).

const scope = { org: 'acme', project: 'billing', view: 'default' }

const items: Record<string, ViewImportItem> = {
  annotation: { Kind: 'Annotation', AnnotationKey: 'rst.payments', Path: 'annotations/rst.payments.json' },
  configuration: { Kind: 'Configuration', AnnotationKey: 'rst.payments', Path: 'rst.payments.conf.json' },
  template: { Kind: 'Template', AnnotationType: 'exe', Path: 'templates/exe.template.json' },
  typeSchema: { Kind: 'TypeSchema', AnnotationType: 'exe', Path: 'schemas/exe.schema.json' },
  annotationSchema: { Kind: 'AnnotationSchema', AnnotationKey: 'rst.payments', Path: 'rst.payments.schema.json' },
  descendantSchema: {
    Kind: 'DescendantTypeSchema',
    AnnotationType: 'exe',
    AnnotationKey: 'sbt.northwind',
    Path: 'schemas/sbt.northwind.exe.schema.json',
  },
}

describe('documents of an import', () => {
  it('names a document by its key, its type code, or both', () => {
    expect(documentName(items.annotation!)).toBe('rst.payments')
    expect(documentName(items.configuration!)).toBe('rst.payments')
    expect(documentName(items.template!)).toBe('exe')
    expect(documentName(items.typeSchema!)).toBe('exe')
    expect(documentName(items.annotationSchema!)).toBe('rst.payments')
    expect(documentName(items.descendantSchema!)).toBe('exe below sbt.northwind')
    // a file that failed before its name was read has only its path
    expect(documentName({ Kind: 'Template', Path: 'broken.template.json' })).toBe('broken.template.json')
  })

  it('links each kind to its page', () => {
    const base = '/acme/billing/default'

    expect(documentPath(scope, items.annotation!)).toBe(`${base}/annotations/rst.payments`)
    expect(documentPath(scope, items.configuration!)).toBe(`${base}/configurations/rst.payments`)
    expect(documentPath(scope, items.template!)).toBe(`${base}/templates/exe`)
    expect(documentPath(scope, items.typeSchema!)).toBe(`${base}/schemas/type/exe`)
    expect(documentPath(scope, items.annotationSchema!)).toBe(`${base}/schemas/annotation/rst.payments`)
    expect(documentPath(scope, items.descendantSchema!)).toBe(`${base}/schemas/annotation/sbt.northwind/type/exe`)
    expect(documentPath(scope, { Kind: 'Template' })).toBeUndefined()
    expect(exportImportPath(scope)).toBe(`${base}/export-import`)
  })

  it('links a document only when the view has it', () => {
    const real: ViewImport = { IsDryRun: false }
    const dryRun: ViewImport = { IsDryRun: true }

    expect(documentExists(real, { Outcome: 'Created' })).toBe(true)
    expect(documentExists(dryRun, { Outcome: 'Created' })).toBe(false)
    expect(documentExists(dryRun, { Outcome: 'Updated' })).toBe(true)
    expect(documentExists(real, { Outcome: 'Skipped', ErrorCode: 'DocumentExists' })).toBe(true)
    expect(documentExists(real, { Outcome: 'Skipped', ErrorCode: 'AnnotationNotFound' })).toBe(false)
    expect(documentExists(real, { Outcome: 'Failed', ErrorCode: 'SchemaViolation' })).toBe(false)
  })

  it('maps each document kind to the kind it is included by', () => {
    expect(contentOf('Annotation')).toBe('Annotations')
    expect(contentOf('TypeSchema')).toBe('Schemas')
    expect(contentOf('DescendantTypeSchema')).toBe('Schemas')
    expect(contentOf('AnnotationSchema')).toBe('Schemas')
    expect(contentOf('Template')).toBe('Templates')
    expect(contentOf('Configuration')).toBe('Configurations')
  })

  it('counts an export by the kinds it includes', () => {
    expect(exportCounts({ AnnotationCount: 12, TemplateCount: 1 })).toBe('12 annotations and 1 template')
    expect(exportCounts({ AnnotationCount: 1, SchemaCount: 2, TemplateCount: 0, ConfigurationCount: 3 })).toBe(
      '1 annotation, 2 schemas, 0 templates and 3 configurations',
    )
    expect(exportCounts({ ConfigurationCount: null })).toBe('')
    expect(joinList(['a'])).toBe('a')
  })
})

describe('what an import wrote', () => {
  const written = (item: ViewImportItem, outcome: ViewImportItem['Outcome'] = 'Updated') => ({
    ...item,
    Outcome: outcome,
  })

  it('reads the kinds from the summaries and the items, and nothing from a dry run', () => {
    const state: ViewImport = {
      IsDryRun: false,
      Summaries: [
        { Content: 'Annotations', Created: 0, Updated: 0, Unchanged: 4 },
        { Content: 'Templates', Updated: 1 },
      ],
      Items: [written(items.configuration!, 'Created'), { ...items.typeSchema!, Outcome: 'Failed' }],
    }

    expect([...writtenContents(state)].toSorted()).toEqual(['Configurations', 'Templates'])
    expect(writtenConfigurations(state)).toEqual(['rst.payments'])
    expect(writtenContents({ ...state, IsDryRun: true }).size).toBe(0)
    expect(writtenConfigurations({ ...state, IsDryRun: true })).toEqual([])
  })

  it('knows when annotations were re-created', () => {
    const recreated: ViewImport = {
      Items: [
        {
          ...written(items.annotation!, 'Created'),
          Warnings: [{ Code: 'AnnotationRecreated', Message: 'rst.payments is re-created.' }],
        },
      ],
    }

    expect(recreatedAnnotations(recreated)).toBe(true)
    expect(recreatedAnnotations({ ...recreated, IsDryRun: true })).toBe(false)
    expect(recreatedAnnotations({ Items: [written(items.annotation!)] })).toBe(false)
  })
})

describe('invalidateImport', () => {
  const base = ['v1', 'acme', 'billing', 'default']

  async function invalidated(state: ViewImport) {
    const client = new QueryClient()
    const spy = vi.spyOn(client, 'invalidateQueries')
    const written = await invalidateImport(client, scope, state)
    const keys = spy.mock.calls.map(([filters]) =>
      (filters as { queryKey: string[] }).queryKey.slice(base.length).join('/'),
    )
    return { written, keys: [...new Set(keys)].toSorted() }
  }

  it('refreshes the annotations of a written annotation, without touching configurations', async () => {
    const { written, keys } = await invalidated({ Items: [{ ...items.annotation!, Outcome: 'Updated' }] })

    expect([...written]).toEqual(['Annotations'])
    expect(keys).toEqual(['annotations'])
  })

  it('refreshes the schemas and every configuration after a schema', async () => {
    const { keys } = await invalidated({ Items: [{ ...items.descendantSchema!, Outcome: 'Created' }] })

    expect(keys).toEqual(['configuration', 'configuration-schema', 'configuration-schemas', 'configurations'])
  })

  it('refreshes the templates and every effective document after a template, without an undefined key', async () => {
    const { keys } = await invalidated({ Items: [{ ...items.template!, Outcome: 'Updated' }] })

    expect(keys).toEqual(['configuration', 'configurations', 'template', 'templates'])
    expect(keys.some((key) => key.includes('undefined'))).toBe(false)
  })

  it('refreshes the configurations it wrote, and every one when the items are truncated', async () => {
    const state: ViewImport = {
      Summaries: [{ Content: 'Configurations', Updated: 1 }],
      Items: [{ ...items.configuration!, Outcome: 'Updated' }],
    }

    expect((await invalidated(state)).keys).toEqual([
      'annotations/rst.payments/history',
      'configuration/rst.payments',
      'configurations',
    ])
    expect((await invalidated({ ...state, AreItemsTruncated: true })).keys).toEqual([
      'annotations/rst.payments/history',
      'configuration',
      'configuration/rst.payments',
      'configurations',
    ])
  })

  it('refreshes every configuration after a re-create, and nothing after a dry run or an unchanged import', async () => {
    const recreated = await invalidated({
      Items: [
        {
          ...items.annotation!,
          Outcome: 'Created',
          Warnings: [{ Code: 'AnnotationRecreated', Message: 'rst.payments is re-created.' }],
        },
      ],
    })

    expect(recreated.keys).toEqual(['annotations', 'configuration', 'configurations'])
    expect((await invalidated({ IsDryRun: true, Items: [{ ...items.template!, Outcome: 'Updated' }] })).keys).toEqual(
      [],
    )
    expect((await invalidated({ Summaries: [{ Content: 'Schemas', Unchanged: 3 }], Items: [] })).keys).toEqual([])
  })
})
