import { describe, expect, it } from 'vitest'
import { effectiveContent } from '@/mock/configuration-handlers'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// fabrikam/invoicing-suite follows docs/42for.net/platform/examples/b2b-saas.md and example 3 of the Miro board.

const showcase = showcaseOf('fabrikam', 'invoicing-suite')

describe('fabrikam/invoicing-suite', () => {
  it('has the catalog of the documentation and of the board', () => {
    const keys = showcase.keys('default')
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
    expect(showcase.own('default', 'usg.northwind.invoicing')).toMatchObject({ plan: 'enterprise' })
    expect(showcase.own('default', 'usg.contoso.invoicing')).toMatchObject({ plan: 'standard' })
    expect(showcase.own('default', 'exe.northwind.invoicing.production')).toMatchObject({ taxProfile: 'no-mva-custom' })
    expect(showcase.own('default', 'cnt.northwind.production')).toMatchObject({ region: 'norwayeast' })
    expect(showcase.own('default', 'sbt.northwind')).toMatchObject({
      legalName: 'Northwind Traders AS',
      residency: 'eu',
    })
  })

  it('moves Northwind’s export to 02:00 in Oslo and switches off the sandbox one', () => {
    expect(showcase.own('default', 'uxe.northwind.invoicing.production.nightly-export')).toEqual({
      cron: '0 2 * * *',
      timeZone: 'Europe/Oslo',
    })
    const sandbox = showcase.annotations('default').get('uxe.northwind.invoicing.sandbox.nightly-export')!
    expect(annotationStatus(sandbox)).toBe('disabled')
    // Contoso keeps the module's schedule: its job run has no document of its own and inherits the template.
    const contoso = 'uxe.contoso.invoicing.production.nightly-export'
    expect(showcase.own('default', contoso)).toBeUndefined()
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), contoso)).toMatchObject({
      format: 'csv',
      retries: 2,
    })
  })

  it('requires a plan on invoicing contracts and a region on Northwind environments', () => {
    expect(showcase.latest('default', 'schema:descendant:rst.invoicing:usg')).toMatchObject({ required: ['plan'] })
    expect(showcase.latest('default', 'schema:descendant:sbt.northwind:cnt')).toMatchObject({ required: ['region'] })
  })

  it('follows the board in development: Northwind’s sandbox and a new QA environment', () => {
    const keys = showcase.keys('development')
    expect([...keys].filter((key) => key.startsWith('sbt.'))).toEqual(['sbt.northwind'])
    expect(keys.has('cnt.northwind.qa')).toBe(true)
    expect(keys.has('exe.northwind.portal.qa')).toBe(true)
    expect(keys.has('uxe.northwind.invoicing.qa.nightly-export')).toBe(true)
    expect(keys.has('cnt.northwind.production')).toBe(false)
  })

  it('brings PEPPOL and the e-archive module in the release candidate', () => {
    expect(showcase.own('release-candidate', 'rst.invoicing')?.features).toContain('peppol')
    expect(showcase.keys('release-candidate').has('exe.northwind.e-archive.production')).toBe(true)
    expect(showcase.keys('default').has('rst.e-archive')).toBe(false)
  })

  it('starts with a deleted sandbox and its deployments, and a deployment deleted alone', () => {
    expect(showcase.trash('default').get('cnt.trey-research.sandbox')).toEqual(
      expect.arrayContaining(['exe.trey-research.invoicing.sandbox', 'cnt.trey-research.sandbox']),
    )
    expect(showcase.trash('default').get('exe.vanarsdel.portal.production')).toEqual([
      'exe.vanarsdel.portal.production',
    ])
  })
})
