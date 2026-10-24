import { describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import AnnotationForm from '@/components/annotations/AnnotationForm.vue'
import { useCatalogStore } from '@/stores/catalog'
import type { CatalogStatus } from '@/stores/catalog'
import { mountScreen, settle } from '../../helpers'

const scope = { org: 'acme', project: 'billing', view: 'default' }
const executions = ['alpha', 'bravo', 'charlie', 'delta', 'echo', 'foxtrot'].map(
  (name) => `exe.northwind.${name}.prod`,
)

const context: Annotation = {
  ProjectName: 'billing',
  ViewName: 'default',
  AnnotationKey: 'cnt.northwind.prod',
  Name: 'prod',
  AnnotationType: 'Context',
  ...({
    SubjectKey: 'sbt.northwind',
    ResponsibilityNames: ['secret-name'],
    UnitType: 'event',
  } as object),
}

const catalogKeys = [
  'sbt.northwind',
  'cnt.northwind.prod',
  ...executions,
  'uxe.northwind.alpha.prod.worker',
  'exe.northwind.shipping.stage',
]

function fillCatalog(
  keys: string[],
  state: { status?: CatalogStatus; error?: unknown; fromSnapshot?: boolean; scope?: typeof scope | null } = {},
): void {
  const catalog = useCatalogStore()
  catalog.scope = state.scope === undefined ? scope : state.scope
  catalog.status = state.status ?? 'ready'
  catalog.error = state.error
  catalog.fromSnapshot = state.fromSnapshot ?? false
  catalog.loadedAt = 1
  catalog.byKey = new Map(
    keys.map((key) => [
      key,
      {
        ProjectName: 'billing',
        ViewName: 'default',
        AnnotationKey: key,
        Name: key.split('.').at(-1)!,
        AnnotationType: 'Execution',
      } satisfies Annotation,
    ]),
  )
  catalog.revision++
}

async function mount(path = `/${scope.org}/${scope.project}/${scope.view}/annotations/cnt.northwind.prod?mode=tree`) {
  const screen = await mountScreen(AnnotationForm, { annotation: context, fresh: true }, { path, scope })
  await settle()
  return screen
}

describe('annotation relations', () => {
  it('links ancestors and replaces name badges with descendant groups', async () => {
    fillCatalog(catalogKeys)
    const screen = await mount()

    expect(screen.host.text()).not.toContain('secret-name')
    expect(screen.host.text()).toContain('Ancestors')
    expect(screen.host.text()).toContain('UnitType')
    expect(screen.host.text()).toContain('event')

    const ancestor = screen.host.findAll('a').find((link) => link.text() === 'sbt.northwind')
    expect(decodeURIComponent(ancestor?.attributes('href') ?? '')).toContain(
      '/acme/billing/default/annotations/sbt.northwind',
    )
    expect(ancestor?.attributes('href')).toContain('mode=tree')

    const execution = screen.host.get('[data-testid="descendant-group"]')
    expect(execution.text()).toContain('Execution')
    expect(execution.text()).toContain('6')
    expect(execution.text()).toContain('exe.northwind.alpha.prod')
    expect(execution.text()).not.toContain('exe.northwind.foxtrot.prod')
    expect(execution.text()).not.toContain('exe.northwind.shipping.stage')

    const preview = execution.findAll('a').find((link) => link.text() === 'exe.northwind.alpha.prod')
    expect(decodeURIComponent(preview?.attributes('href') ?? '')).toContain(
      '/acme/billing/default/annotations/exe.northwind.alpha.prod',
    )
    expect(preview?.attributes('href')).toContain('mode=tree')

    const showAll = screen.host.findAll('a').find((link) => link.text() === 'Show all 6')
    const showAllHref = decodeURIComponent(showAll?.attributes('href') ?? '').replaceAll('+', ' ')
    expect(showAllHref).toContain('/acme/billing/default/annotations?')
    expect(showAllHref).toContain('q=ancestor:cnt.northwind.prod type:exe')
    expect(showAllHref).not.toContain('mode=')
    expect(showAllHref).not.toContain('/cnt.northwind.prod')

    const all = screen.host.findAll('a').find((link) => link.text().startsWith('All descendants'))
    expect(decodeURIComponent(all?.attributes('href') ?? '').replaceAll('+', ' ')).toContain(
      'q=ancestor:cnt.northwind.prod',
    )
    expect(all?.text()).toBe('All descendants (7)')
    screen.host.unmount()
  })

  it('hides counts while the catalog is loading or for another view', async () => {
    fillCatalog(catalogKeys, { status: 'loading' })
    const loading = await mount()
    expect(loading.host.get('[data-testid="descendants-status"]').text()).toBe('Counting descendants…')
    expect(loading.host.text()).not.toContain('exe.northwind.alpha.prod')
    expect(loading.host.text()).not.toContain('at least')
    loading.host.unmount()

    fillCatalog(catalogKeys, { scope: { org: 'other', project: 'billing', view: 'default' } })
    const other = await mount()
    expect(other.host.get('[data-testid="descendants-status"]').text()).toBe('Counting descendants…')
    expect(other.host.text()).not.toContain('Show all')
    other.host.unmount()
  })

  it('marks a partial catalog as at least', async () => {
    fillCatalog(catalogKeys, { error: new Error('page failed') })
    const screen = await mount()

    expect(screen.host.get('[data-testid="descendants-status"]').text()).toContain('incomplete')
    expect(screen.host.text()).toContain('at least 6')
    expect(screen.host.text()).toContain('at least 7')
    expect(screen.host.findAll('a').some((link) => link.text() === 'Show all 6')).toBe(true)
    screen.host.unmount()
  })

  it('omits descendants when the annotation cannot have any', async () => {
    const leaf: Annotation = {
      ...context,
      AnnotationKey: 'uxe.northwind.alpha.prod.worker',
      Name: 'worker',
      AnnotationType: 'UnitOfExecution',
    }
    const open = async (state: Parameters<typeof fillCatalog>[1] = {}) => {
      fillCatalog(['uxe.northwind.alpha.prod.worker'], state)
      const screen = await mountScreen(AnnotationForm, { annotation: leaf, fresh: true }, { scope })
      await settle()
      expect(screen.host.text()).toContain('Ancestors')
      expect(screen.host.text()).not.toContain('Descendants')
      expect(screen.host.text()).not.toContain('Counting descendants')
      screen.host.unmount()
    }

    await open()
    await open({ status: 'loading' })
    await open({ status: 'error', error: new Error('page failed') })
  })
})
