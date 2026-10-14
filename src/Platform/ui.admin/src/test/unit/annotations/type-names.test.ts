import { glob, readFile } from 'node:fs/promises'
import { afterEach, describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import AnnotationTypeBadge from '@/components/annotations/AnnotationTypeBadge.vue'
import DeletedKeysDialog from '@/components/annotations/DeletedKeysDialog.vue'
import GraphTooltip from '@/components/graph/GraphTooltip.vue'
import { applyTypeAppearance } from '@/utils/type-appearance'
import { mountInApp, settle } from '../../helpers'

// The type chips show the names of the settings, not the codes (424-34); only Schemas keeps the codes.

// A chip without a variant shows the code: the tags in templates and the props of render functions.
const badgeRE = /<AnnotationTypeBadge\b[^>]*>|h\(AnnotationTypeBadge,\s*\{[^}]*\}/g

const deleted = (key: string, title: string) => ({ AnnotationKey: key, Title: title }) as Annotation

describe('type names instead of codes', () => {
  afterEach(() => {
    // typeMeta is shared by the whole module graph: put the built-in names back for the next test.
    applyTypeAppearance({})
  })

  it('names the types in the Deleted annotations dialog', async () => {
    applyTypeAppearance({ sbt: { label: 'Customer' } })
    const view = mountInApp(DeletedKeysDialog, { open: true, deleted: [deleted('sbt.contoso', 'Contoso subject')] })
    await settle(10)

    const list = document.body.querySelector('[aria-label="Deleted annotations"]')!
    expect(list.querySelector('[title="Customer"]')?.textContent?.trim()).toBe('Customer')
    expect(list.textContent).toContain('sbt.contoso')
    view.host.unmount()
  })

  it('names the type in the hover card of a graph node', async () => {
    applyTypeAppearance({ exe: { label: 'Job' } })
    const view = mountInApp(GraphTooltip, {
      annotationKey: 'exe.northwind.invoicing.prod',
      x: 10,
      y: 10,
      bounds: { width: 800, height: 600 },
      neighbours: 2,
    })
    await settle()

    const card = document.body.querySelector('[role="tooltip"]')!
    expect(card.querySelector('[title="Job"]')?.textContent?.trim()).toBe('Job')
    expect(card.textContent).toContain('exe.northwind.invoicing.prod')
    view.host.unmount()
  })

  it('cuts a long name with an ellipsis and keeps the whole name in the title', async () => {
    const name = 'Very long execution type name for tests'
    applyTypeAppearance({ exe: { label: name } })
    const view = mountInApp(AnnotationTypeBadge, { type: 'exe', variant: 'label' })
    await settle()

    const chip = view.inner().element as HTMLElement
    expect(chip.title).toBe(name)
    expect(chip.classList).toContain('max-w-full')
    expect(chip.querySelector('.truncate')?.textContent).toBe(name)
    view.host.unmount()
  })

  it('shows the codes only on Schemas', async () => {
    const chips: string[] = []
    for await (const file of glob('app/**/*.{vue,ts}', { exclude: (name) => name.endsWith('.d.ts') })) {
      const source = await readFile(file, 'utf8')
      for (const match of source.matchAll(badgeRE)) {
        if (!match[0].includes('variant')) {
          chips.push(file.replaceAll('\\', '/'))
        }
      }
    }

    expect(chips).toEqual(['app/pages/[org]/[project]/[view]/schemas.vue'])
  })
})
