import { afterEach, describe, expect, it } from 'vitest'
import type { ViewInboxItem } from '@42for.net/2splatform.sdk'
import InboxItemRow from '@/components/inbox/InboxItemRow.vue'
import { mountScreen, settle } from '../../helpers'

// One row of the Inbox page (424-31): what it shows of an item, and the buttons of what the item offers. The row runs
// nothing itself: it tells the page which button was pressed.

const hour = 3_600_000
/** A time in the past, or with a negative span in the future; five minutes more, so the hours stay whole in a test. */
const ago = (ms: number) => new Date(Date.now() - ms - Math.sign(ms) * 300_000).toISOString()

const system = { Kind: 'System', Id: 'system' } as const
const ada = { Kind: 'Account', Id: 'user_01MOCKADA', Name: 'Ada Lovelace' } as const
const grace = { Kind: 'Account', Id: 'user_01MOCKGRACE', Name: 'Grace Hopper' } as const

const action: ViewInboxItem = {
  Id: '2026112208000000000003f2b8c1d4e5f4a6b9c0d1e2f3a4b5c6d',
  Kind: 'Action',
  Topic: 'SchemaSuggestion',
  Level: 'Info',
  Title: 'Generate a schema for 4 similar configurations',
  Text: '4 configurations of this view have the same members.',
  MinimalRole: 'Contributor',
  State: 'Open',
  CreatedAt: ago(2 * hour),
  CreatedBy: system,
  ExpiresAt: ago(-24 * hour),
  Action: 'GenerateSchema',
}

const exported: ViewInboxItem = {
  Id: '20261122091407000000079aab47ca406989d11506daca106041b',
  Kind: 'Notification',
  Topic: 'ViewExport',
  Level: 'Success',
  Title: 'Export finished',
  Text: '12 annotations, 512 B',
  MinimalRole: 'Administrator',
  State: 'Open',
  CreatedAt: ago(3 * hour),
  CreatedBy: { ...ada, Via: 'mcp' },
  ExpiresAt: ago(-21 * hour),
  Job: { Kind: 'ViewExport', Id: '5f0c2d7e9a414b6c8e3d1f2a4b6c8d0e', FileName: 'export-acme-billing-default.zip' },
}

const imported: ViewInboxItem = {
  ...exported,
  Topic: 'ViewImport',
  Title: 'Import finished',
  Text: 'acme.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed',
  CreatedBy: ada,
  Job: { Kind: 'ViewImport', Id: '9b1d4c7a2e5f4083a6c9d2e5f8b1a4c7', FileName: 'acme.zip' },
}

const mounted: { host: { unmount: () => void } }[] = []
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  document.body.innerHTML = ''
})

async function mountRow(item: ViewInboxItem, props: Record<string, unknown> = {}, accessMap?: Record<string, string>) {
  const view = await mountScreen(InboxItemRow, { item, ...props }, { accessMap })
  mounted.push(view)
  await settle()
  const root = view.inner().element as HTMLElement
  const text = () => root.textContent?.replace(/\s+/g, ' ').trim() ?? ''
  const buttons = () => [...root.querySelectorAll('button')]
  const button = (label: string) => buttons().find((candidate) => candidate.textContent?.trim() === label)
  return { ...view, root, text, buttons, button }
}

describe('InboxItemRow', () => {
  it('shows the title and the text of the producer, when the item arrived and who caused it', async () => {
    const { root, text } = await mountRow(action)

    expect(text()).toContain('Generate a schema for 4 similar configurations')
    expect(text()).toContain('4 configurations of this view have the same members.')
    expect(text()).toContain('2 hours ago by System')
    expect(root.querySelector('time')?.getAttribute('datetime')).toBe(action.CreatedAt)
  })

  it('says the kind of the item in words too, next to the icon of its kind', async () => {
    const { root } = await mountRow(action)

    expect(root.querySelector('.sr-only')?.textContent).toBe('Suggested action:')
    expect(root.querySelector('[title="Suggested action"] .text-primary')).not.toBeNull()
  })

  it('shows a failed job by its level, and offers only Dismiss', async () => {
    const { root, buttons } = await mountRow({ ...exported, Level: 'Error', Title: 'Export failed' })

    expect(root.querySelector('.sr-only')?.textContent).toBe('Failed:')
    expect(root.querySelector('[title="Failed"] .text-error')).not.toBeNull()
    expect(buttons().map((candidate) => candidate.textContent?.trim())).toEqual(['Dismiss'])
  })

  it('marks an item of Administrators, and not one of Contributors', async () => {
    expect((await mountRow(exported)).text()).toContain('Administrators only')
    expect((await mountRow(action)).text()).not.toContain('Administrators only')
  })

  it('names the actor a person acted through, and keeps the name out of the click tracking', async () => {
    const { root, text } = await mountRow(exported)

    expect(text()).toContain('3 hours ago by Ada Lovelace via mcp')
    const name = [...root.querySelectorAll('.ph-no-capture')].find((element) => element.textContent === 'Ada Lovelace')
    // the Owner of the project reaches the person's row on the members page
    expect(name?.getAttribute('href')).toBe('/acme/billing/members?highlight=user_01MOCKADA')
  })

  it.each([
    ['Dismissed', 'Dismissed by Grace Hopper, 1 hour ago'],
    ['Accepted', 'Accepted by Grace Hopper, 1 hour ago'],
  ] as const)('says who closed an item that was %s, and when', async (State, line) => {
    const { text, button } = await mountRow({ ...action, State, Closed: { At: ago(hour), By: grace } })

    expect(text()).toContain(line)
    expect(button('Accept')).toBeUndefined()
    expect(button('Dismiss')).toBeUndefined()
  })

  it('says nothing about a closing on an open item', async () => {
    const { text } = await mountRow(action)

    expect(text()).not.toMatch(/Accepted by|Dismissed by/)
  })

  it('offers Accept and Dismiss for an open action, and tells the page which one was pressed', async () => {
    const view = await mountRow(action)

    expect(view.buttons().map((candidate) => candidate.textContent?.trim())).toEqual(['Accept', 'Dismiss'])
    view.button('Accept')!.click()
    view.button('Dismiss')!.click()

    expect(view.inner().emitted()).toMatchObject({ accept: [[]], dismiss: [[]] })
    expect(view.inner().emitted('download')).toBeUndefined()
    expect(view.inner().emitted('result')).toBeUndefined()
  })

  it('does not take a second accept or dismiss while one is on its way', async () => {
    const view = await mountRow(action, { busy: true })

    expect(view.button('Accept')!.disabled).toBe(true)
    expect(view.button('Dismiss')!.disabled).toBe(true)
    view.button('Accept')!.click()
    view.button('Dismiss')!.click()

    expect(view.inner().emitted('accept')).toBeUndefined()
    expect(view.inner().emitted('dismiss')).toBeUndefined()
  })

  it('offers Download with the expiry for an export that succeeded, also once it is done', async () => {
    const open = await mountRow(exported)

    expect(open.buttons().map((candidate) => candidate.textContent?.trim())).toEqual(['Download', 'Dismiss'])
    expect(open.text()).toContain('Expires in 21 hours')
    open.button('Download')!.click()
    expect(open.inner().emitted('download')).toHaveLength(1)

    const done = await mountRow({ ...exported, State: 'Dismissed', Closed: { At: ago(hour), By: ada } })
    expect(done.buttons().map((candidate) => candidate.textContent?.trim())).toEqual(['Download'])
    expect(done.text()).toContain('Expires in 21 hours')
  })

  it('shows that the archive is being fetched, and takes no second Download meanwhile', async () => {
    const view = await mountRow(exported, { downloading: true })

    expect(view.button('Download')!.disabled).toBe(true)
  })

  it('offers Show the result for an import that ran, without an expiry', async () => {
    const view = await mountRow(imported)

    expect(view.buttons().map((candidate) => candidate.textContent?.trim())).toEqual(['Show the result', 'Dismiss'])
    expect(view.text()).not.toContain('Expires')
    view.button('Show the result')!.click()
    expect(view.inner().emitted('result')).toHaveLength(1)
  })

  it('has no buttons for a closed item that offers nothing', async () => {
    const { buttons } = await mountRow({
      ...imported,
      State: 'Dismissed',
      Job: undefined,
      Closed: { At: ago(hour), By: grace },
    })

    expect(buttons()).toEqual([])
  })

  it('shows the name of a person without a link to somebody who does not manage the access', async () => {
    const { root } = await mountRow(imported, {}, { 'acme.billing': 'Contributor' })

    const name = [...root.querySelectorAll('.ph-no-capture')].find((element) => element.textContent === 'Ada Lovelace')
    expect(name).toBeTruthy()
    expect(name?.tagName).not.toBe('A')
  })
})
