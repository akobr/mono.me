import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { SetupContext } from 'vue'
import { setupServer } from 'msw/node'
import { deleteAnnotation } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import DeletedPage from '@/pages/[org]/[project]/[view]/modeling/deleted.vue'
import { mountScreen, settle } from '../../helpers'

// The Deleted tab of Modeling (spec 4.5): a Reader browses the deleted annotations and their details read-only, without
// the selection, which only serves the bulk actions; a Contributor selects and restores them (424-272).

// the navbar needs the dashboard shell; the stand-in shows its title and what the page puts on its right
vi.mock('@/components/shell/AppNavbar.vue', async () => {
  const vue = await import('vue')
  return {
    default: {
      name: 'AppNavbar',
      props: ['title'],
      setup:
        (props: { title?: string }, { slots }: SetupContext) =>
        () =>
          vue.h('header', { 'data-navbar': props.title }, slots.right?.()),
    },
  }
})

const server = setupServer(...mockHandlers)
// unmounted before the body is cleared, so a closing menu or tooltip does not patch removed nodes
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.events.removeAllListeners()
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const [org, project, view] = ['acme', 'billing', 'default']
const roots = ['sbt.contoso', 'sbt.northwind']

/**
 * Deletes two subjects, with their descendants. The mock API reads the role of its own database, where the mock user
 * owns acme.billing, so the seed is allowed whatever role the page is mounted with.
 */
async function seedDeletions() {
  for (const key of roots) {
    await deleteAnnotation(org, project, view, key)
  }
}

async function mountDeleted(role: string) {
  await seedDeletions()
  const screen = await mountScreen(
    DeletedPage,
    {},
    { path: `/${org}/${project}/${view}/modeling/deleted`, accessMap: { [`${org}.${project}`]: role } },
  )
  mounted.push(screen)
  await vi.waitFor(() => expect(rowKeys()).toHaveLength(roots.length))
  return screen
}

const text = () => document.body.textContent ?? ''
const rowKeys = () =>
  [...document.body.querySelectorAll('tbody button[aria-label^="Show "]')].map((button) =>
    button.getAttribute('aria-label')!.slice('Show '.length),
  )
const checkboxes = () => [...document.body.querySelectorAll('[aria-label^="Select "]')]
const checkbox = (key: string) => document.body.querySelector<HTMLElement>(`[aria-label="Select ${key}"]`)
const bulkBar = () => document.body.querySelector('[role="toolbar"][aria-label="Bulk actions"]')
const buttonsOf = (root: ParentNode | null) =>
  [...(root?.querySelectorAll('button') ?? [])].map((button) => button.textContent?.trim())

/** Opens the row menu of a key and returns the labels of its items. */
async function openRowMenu(key: string) {
  document.body.querySelector<HTMLElement>(`[aria-label="Actions for ${key}"]`)!.click()
  await settle()
  return [...document.body.querySelectorAll('[role="menu"] [role="menuitem"]')].map((item) => item.textContent?.trim())
}

describe('the Deleted tab of a Reader', () => {
  it('lists the deleted annotations without the selection or the bulk actions', async () => {
    await mountDeleted('Reader')

    expect(rowKeys().toSorted()).toEqual(roots)
    expect(text()).toContain('2 shown')
    expect(document.body.querySelector('[aria-label="Select all"]')).toBeNull()
    expect(checkboxes()).toEqual([])
    expect(bulkBar()).toBeNull()
    // the header keeps the four tabs of the role
    expect([...document.body.querySelectorAll('[role="tab"]')].map((tab) => tab.textContent?.trim())).toEqual([
      'Schemas',
      'Templates',
      'Deleted',
      'Compare views',
    ])
  })

  it('offers only Show details in the menu of a row, and the detail without Restore', async () => {
    await mountDeleted('Reader')

    expect(await openRowMenu('sbt.northwind')).toEqual(['Show details'])

    ;([...document.body.querySelectorAll('[role="menuitem"]')][0] as HTMLElement).click()
    await vi.waitFor(() => expect(text()).toContain('The deleted annotation, read-only, and its history.'))
    await settle(10)
    const detail = document.body.querySelector('[role="dialog"]')
    expect(detail?.querySelector('[aria-label="Annotation document"]')?.textContent).toContain('"sbt.northwind"')
    expect(buttonsOf(detail)).not.toContain('Restore')
  })
})

describe('the Deleted tab of a Contributor', () => {
  it('selects rows and restores them from the bulk actions, without Delete permanently', async () => {
    const screen = await mountDeleted('Contributor')

    expect(document.body.querySelector('[aria-label="Select all"]')).not.toBeNull()
    expect(checkboxes()).toHaveLength(roots.length + 1)
    expect(bulkBar()).toBeNull()

    checkbox('sbt.northwind')!.click()
    await settle()
    expect(bulkBar()?.textContent).toContain('1 selected')
    expect(buttonsOf(bulkBar())).toEqual(['Restore', 'Clear selection'])

    ;[...bulkBar()!.querySelectorAll<HTMLElement>('button')]
      .find((button) => button.textContent?.trim() === 'Restore')!
      .click()
    await vi.waitFor(() => expect(text()).toContain('sbt.northwind restored'))
    await vi.waitFor(() => expect(rowKeys()).toEqual(['sbt.contoso']))
    expect(bulkBar()).toBeNull()
    expect(screen.router.currentRoute.value.path).toBe('/acme/billing/default/modeling/deleted')
  })

  it('offers Restore with and without descendants in the menu of a row, not Delete permanently', async () => {
    await mountDeleted('Contributor')

    expect(await openRowMenu('sbt.northwind')).toEqual(['Show details', 'Restore', 'Restore without descendants'])
  })
})
