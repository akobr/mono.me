import { beforeEach, describe, expect, it } from 'vitest'
import { effectiveContent } from '@/mock/configuration-handlers'
import { annotationsOf, resetDb, type MockDb } from '@/mock/db'
import { showcaseScope } from '@/mock/showcase'
import { annotationStatus } from '@/utils/annotation-status'

// fabrikam/invoicing-suite follows docs/42for.net/platform/examples/b2b-saas.md and example 3 of the Miro board.

const [org, project] = ['fabrikam', 'invoicing-suite']
const scope = (view: string) => ({ org, project, view })
const keysOf = (view: string) => new Set(showcaseScope(org, project, view)!.annotations.map((a) => a.AnnotationKey))
const own = (view: string, key: string) =>
  showcaseScope(org, project, view)!.documents.get(`configuration:${key}`)?.versions.at(-1)?.Content

let db: MockDb
beforeEach(() => {
  db = resetDb('showcase')
})

describe('fabrikam/invoicing-suite', () => {
  it('has the catalog of the documentation and of the board', () => {
    const keys = keysOf('default')
    for (const key of [
      'sbt.northwind',
      'sbt.contoso',
      'cnt.northwind.production',
      'cnt.northwind.sandbox',
      'cnt.contoso.production',
      'rst.invoicing',
      'rst.payments',
      'rst.portal',
      'usg.northwind.invoicing',
      'usg.northwind.payments',
      'usg.northwind.portal',
      'usg.contoso.invoicing',
      'exe.northwind.invoicing.production',
      'exe.northwind.invoicing.sandbox',
      'exe.contoso.invoicing.production',
      'exe.northwind.payments.production',
      'exe.northwind.portal.production',
      'exe.northwind.portal.sandbox',
      'unt.invoicing.nightly-export',
      'uxe.northwind.invoicing.production.nightly-export',
      'uxe.northwind.invoicing.sandbox.nightly-export',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    // Contoso buys invoicing only, in a single production instance.
    expect([...keys].filter((key) => key.startsWith('usg.contoso.'))).toEqual(['usg.contoso.invoicing'])
    expect([...keys].filter((key) => key.startsWith('cnt.contoso.'))).toEqual(['cnt.contoso.production'])
  })

  it('stores the plans on the usages and the one-off on the production execution', () => {
    expect(own('default', 'usg.northwind.invoicing')).toMatchObject({ plan: 'enterprise' })
    expect(own('default', 'usg.contoso.invoicing')).toMatchObject({ plan: 'standard' })
    expect(own('default', 'exe.northwind.invoicing.production')).toMatchObject({ taxProfile: 'no-mva-custom' })
    expect(own('default', 'cnt.northwind.production')).toMatchObject({ region: 'norwayeast' })
    expect(own('default', 'sbt.northwind')).toMatchObject({ legalName: 'Northwind Traders AS', residency: 'eu' })
  })

  it('moves Northwind’s export to 02:00 in Oslo and switches off the sandbox one', () => {
    expect(own('default', 'uxe.northwind.invoicing.production.nightly-export')).toEqual({
      cron: '0 2 * * *',
      timeZone: 'Europe/Oslo',
    })
    const sandbox = annotationsOf(db, org, project, 'default').get('uxe.northwind.invoicing.sandbox.nightly-export')!
    expect(annotationStatus(sandbox)).toBe('disabled')
    // Contoso keeps the module's schedule: its job run has no document of its own and inherits the template.
    expect(own('default', 'uxe.contoso.invoicing.production.nightly-export')).toBeUndefined()
    expect(effectiveContent(db, scope('default'), 'uxe.contoso.invoicing.production.nightly-export')).toMatchObject({
      format: 'csv',
      retries: 2,
    })
  })

  it('requires a plan on invoicing contracts and a region on Northwind environments', () => {
    const documents = showcaseScope(org, project, 'default')!.documents
    expect(documents.get('schema:descendant:rst.invoicing:usg')?.versions.at(-1)?.Content).toMatchObject({
      required: ['plan'],
    })
    expect(documents.get('schema:descendant:sbt.northwind:cnt')?.versions.at(-1)?.Content).toMatchObject({
      required: ['region'],
    })
  })

  it('follows the board in development: Northwind’s sandbox and a new QA environment', () => {
    const keys = keysOf('development')
    expect([...keys].filter((key) => key.startsWith('sbt.'))).toEqual(['sbt.northwind'])
    expect(keys.has('cnt.northwind.qa')).toBe(true)
    expect(keys.has('exe.northwind.portal.qa')).toBe(true)
    expect(keys.has('uxe.northwind.invoicing.qa.nightly-export')).toBe(true)
    expect(keys.has('cnt.northwind.production')).toBe(false)
  })

  it('brings PEPPOL and the e-archive module in the release candidate', () => {
    expect(own('release-candidate', 'rst.invoicing')?.features).toContain('peppol')
    expect(keysOf('release-candidate').has('exe.northwind.e-archive.production')).toBe(true)
    expect(keysOf('default').has('rst.e-archive')).toBe(false)
  })
})
