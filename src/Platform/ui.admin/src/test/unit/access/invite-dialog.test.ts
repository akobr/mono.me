import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { http } from 'msw'
import { setupServer } from 'msw/node'
import type { InvitationCreate } from '@42for.net/2splatform.sdk'
import InvitationsTable from '@/components/access/InvitationsTable.vue'
import InviteDialog from '@/components/access/InviteDialog.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb } from '@/mock/db'
import { mountScreen, settle } from '../../helpers'

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// A failed test must not leave its teleported dialog in the body for the next one.
let unmount: (() => void) | undefined
afterEach(() => {
  unmount?.()
  unmount = undefined
})

describe('InviteDialog', () => {
  /** Opens the dialog for acme.billing and records the bodies of the invitations it posts to the mock API. */
  async function openDialog() {
    const bodies: InvitationCreate[] = []
    server.use(
      // returns nothing, so the mock API behind it answers
      http.post('*/api/v1/access/points/:key/invitations', async ({ request }) => {
        bodies.push((await request.clone().json()) as InvitationCreate)
      }),
    )
    const open = ref(false)
    const Harness = defineComponent({
      setup: () => () =>
        h(InviteDialog, {
          open: open.value,
          'onUpdate:open': (value: boolean) => (open.value = value),
          accessPointKey: 'acme.billing',
        }),
    })
    const screen = await mountScreen(Harness, {})
    unmount = () => screen.host.unmount()
    open.value = true
    await settle(6)
    return { bodies, open }
  }

  const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')!
  const input = () => dialog().querySelector<HTMLInputElement>('input[placeholder="ada@example.com"]')!
  const tags = () => [...dialog().querySelectorAll('[data-slot="item"]')].map((tag) => tag.textContent?.trim())
  const text = () => dialog().textContent!.replace(/\s+/g, ' ')
  const sendButton = () =>
    [...dialog().querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Send invitations')!

  /** Types character by character, as a keyboard does: one input event per character. */
  async function type(value: string) {
    for (const character of value) {
      input().value += character
      input().dispatchEvent(new InputEvent('input', { data: character, inputType: 'insertText', bubbles: true }))
    }
    await settle()
  }

  /** Pastes text; false when the dialog took the paste over (prevented the browser's own). */
  async function paste(value: string) {
    const clipboardData = new DataTransfer()
    clipboardData.setData('text/plain', value)
    const native = input().dispatchEvent(
      new ClipboardEvent('paste', { clipboardData, bubbles: true, cancelable: true }),
    )
    await settle()
    return native
  }

  async function send() {
    sendButton().click()
    await settle(12)
  }

  it('commits the typed address on a space and on a comma', async () => {
    await openDialog()
    await type('Ada@example.com grace@example.com, alan@example.com')

    expect(tags()).toEqual(['ada@example.com', 'grace@example.com'])
    expect(input().value).toBe('alan@example.com')
    expect(text()).toContain('Separate addresses with spaces or commas, or paste a list.')
    expect(text()).toContain('Everyone gets the same role and expiry')
  })

  it('makes one tag per address of a pasted list, without the duplicates', async () => {
    await openDialog()
    await type('ada@example.com ')

    expect(await paste('Ada Lovelace <ADA@example.com>, "Hopper, Grace" <grace@example.com>\nalan@example.com')).toBe(
      false,
    )
    expect(tags()).toEqual(['ada@example.com', 'grace@example.com', 'alan@example.com'])
    expect(input().value).toBe('')

    // a part of an address is a normal paste into the typed text
    await type('hal')
    expect(await paste('@example.com')).toBe(true)
    expect(tags()).toHaveLength(3)
  })

  it('sends one invitation per address, with the same role and expiry, also the address left in the input', async () => {
    const { bodies, open } = await openDialog()
    await type('ada@example.com, grace@example.com alan@example.com')
    await send()

    expect(bodies).toEqual([
      { Email: 'ada@example.com', Role: 'Reader', ExpiresInDays: 7 },
      { Email: 'grace@example.com', Role: 'Reader', ExpiresInDays: 7 },
      { Email: 'alan@example.com', Role: 'Reader', ExpiresInDays: 7 },
    ])
    expect(open.value).toBe(false)
    const invited = (loadDb().invitations ?? []).filter((invitation) => invitation.AccessPointKey === 'acme.billing')
    expect(invited.map((invitation) => invitation.Email)).toEqual(
      expect.arrayContaining(['ada@example.com', 'grace@example.com', 'alan@example.com']),
    )
  })

  it('keeps an address with a pending invitation in the box and sends the others', async () => {
    const { bodies, open } = await openDialog()
    await type('katherine@example.com ada@example.com ')
    await send()

    expect(bodies.map((body) => body.Email)).toEqual(['katherine@example.com', 'ada@example.com'])
    expect(text()).toContain('katherine@example.com: A pending invitation already exists.')
    expect(tags()).toEqual(['katherine@example.com'])
    expect(open.value).toBe(true)
  })

  it('asks for an address when the list is empty and blocks Send on an invalid one', async () => {
    const { bodies } = await openDialog()
    await send()

    expect(text()).toContain('Enter at least one email address.')
    expect(bodies).toEqual([])

    await type('ada@example.com grace.example.com ')
    expect(text()).not.toContain('Enter at least one email address.')
    expect(text()).toContain('Not an email: grace.example.com')
    expect(dialog().querySelector('[data-slot="item"]:last-of-type .text-error')?.textContent).toBe('grace.example.com')
    expect(sendButton().disabled).toBe(true)

    // an invalid address left in the input is not sent either
    const backspace = new KeyboardEvent('keydown', { key: 'Backspace', bubbles: true })
    input().dispatchEvent(backspace)
    input().dispatchEvent(backspace)
    await settle()
    expect(tags()).toEqual(['ada@example.com'])
    await type('alan.example.com')
    await send()
    expect(text()).toContain('Not an email: alan.example.com')
    expect(bodies).toEqual([])
  })
})

describe('InvitationsTable', () => {
  it('lists the invitations without an Invite button, which is in the top bar of the Members pages', async () => {
    const screen = await mountScreen(InvitationsTable, { accessPointKey: 'acme.billing' })
    unmount = () => screen.host.unmount()
    await settle(8)

    const buttons = [...document.body.querySelectorAll('button')].map((button) => button.textContent?.trim())
    expect(document.body.textContent).toContain('katherine@example.com')
    expect(buttons).toContain('Resend')
    expect(buttons).not.toContain('Invite')
  })
})
