import { describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import AnnotationLineage from '@/components/annotations/AnnotationLineage.vue'
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

/** Mounts the lineage with every ancestor of the execution in the catalog but its context, then opens it. */
async function openLineage(props: Record<string, unknown>) {
  const screen = await mountScreen(AnnotationLineage, props)
  const catalog = useCatalogStore()
  for (const key of ['rst.invoicing', 'sbt.northwind', 'usg.northwind.invoicing']) {
    catalog.upsert(annotation(key))
  }
  await settle()

  const button = screen.host.get('button')
  const disabled = button.attributes('disabled') !== undefined
  if (!disabled) {
    await button.trigger('click')
    await settle()
  }

  const ancestors = [...document.body.querySelectorAll('ol > li > :is(a, span.line-through)')].map((element) => ({
    key: element.textContent?.trim(),
    href: element.getAttribute('href'),
  }))
  screen.host.unmount()
  return { disabled, ancestors }
}

describe('AnnotationLineage', () => {
  it('links the ancestors to their configurations in merge order and keeps the query', async () => {
    const { disabled, ancestors } = await openLineage({
      annotationKey: 'exe.northwind.invoicing.prod',
      section: 'configurations',
      query: { q: 'type:exe' },
    })

    expect(disabled).toBe(false)
    expect(ancestors).toEqual([
      { key: 'rst.invoicing', href: '/acme/billing/default/configurations/rst.invoicing?q=type:exe' },
      { key: 'sbt.northwind', href: '/acme/billing/default/configurations/sbt.northwind?q=type:exe' },
      {
        key: 'usg.northwind.invoicing',
        href: '/acme/billing/default/configurations/usg.northwind.invoicing?q=type:exe',
      },
      // missing from the catalog: struck through, without a link
      { key: 'cnt.northwind.prod', href: null },
    ])
  })

  it('links to the annotations without a query by default', async () => {
    const { ancestors } = await openLineage({ annotationKey: 'exe.northwind.invoicing.prod' })

    expect(ancestors.map((ancestor) => ancestor.href)).toEqual([
      '/acme/billing/default/annotations/rst.invoicing',
      '/acme/billing/default/annotations/sbt.northwind',
      '/acme/billing/default/annotations/usg.northwind.invoicing',
      null,
    ])
  })

  it('is disabled without ancestors', async () => {
    const { disabled, ancestors } = await openLineage({ annotationKey: 'sbt.northwind', section: 'configurations' })

    expect(disabled).toBe(true)
    expect(ancestors).toEqual([])
  })
})
