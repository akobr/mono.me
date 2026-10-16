import { describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import AnnotationBreadcrumb from '@/components/annotations/AnnotationBreadcrumb.vue'
import { useCatalogStore } from '@/stores/catalog'
import { typeMeta, type AnnotationTypeCode } from '@/utils/annotation-key'
import { mountScreen, settle } from '../../helpers'

const annotation = (key: string): Annotation => ({
  ProjectName: 'billing',
  ViewName: 'default',
  AnnotationKey: key,
  Name: key.split('.').at(-1)!,
  AnnotationType: typeMeta[key.slice(0, 3) as AnnotationTypeCode].type,
})

/** Mounts the breadcrumb with a loaded catalog that has the subject and the usage, but no context or responsibility. */
async function chainOf(props: Record<string, unknown>) {
  const screen = await mountScreen(AnnotationBreadcrumb, { annotationKey: 'exe.northwind.invoicing.prod', ...props })
  const catalog = useCatalogStore()
  for (const key of ['sbt.northwind', 'usg.northwind.invoicing']) {
    catalog.upsert(annotation(key))
  }
  catalog.status = 'ready'
  await settle()

  const items = screen.host.findAll('nav > :is(a, span)').map((item) => ({
    text: item.text(),
    href: item.attributes('href') ?? null,
    current: item.attributes('aria-current') === 'page',
    missing: item.classes('line-through'),
  }))
  screen.host.unmount()
  return items
}

describe('AnnotationBreadcrumb', () => {
  it('links the parents to their configurations and keeps the query', async () => {
    const items = await chainOf({ perspective: 'subject', section: 'configurations', query: { q: 'type:exe' } })

    expect(items).toEqual([
      {
        text: 'Subject northwind',
        href: '/acme/billing/default/configurations/sbt.northwind?q=type:exe',
        current: false,
        missing: false,
      },
      // missing from the catalog: struck through, without a link
      { text: 'Context prod', href: null, current: false, missing: true },
      { text: 'Execution prod', href: null, current: true, missing: false },
    ])
  })

  it('links to the annotations by default', async () => {
    const items = await chainOf({ perspective: 'subject' })

    expect(items.map((item) => item.href)).toEqual(['/acme/billing/default/annotations/sbt.northwind', null, null])
  })

  it('follows the perspective and leaves out an empty query value', async () => {
    const items = await chainOf({ perspective: 'responsibility', section: 'configurations', query: { q: undefined } })

    expect(items.map(({ text, href }) => ({ text, href }))).toEqual([
      { text: 'Responsibility invoicing', href: null },
      {
        text: 'Usage invoicing',
        href: '/acme/billing/default/configurations/usg.northwind.invoicing',
      },
      { text: 'Execution prod', href: null },
    ])
  })
})
