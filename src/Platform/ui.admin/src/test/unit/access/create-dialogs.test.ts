import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref, type Component } from 'vue'
import { setupServer } from 'msw/node'
import type { AccessPointCreate } from '@42for.net/2splatform.sdk'
import OrganizationCreateDialog from '@/components/orgs/OrganizationCreateDialog.vue'
import ProjectCreateDialog from '@/components/orgs/ProjectCreateDialog.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb } from '@/mock/db'
import { mountScreen, settle } from '../../helpers'

// "New project" on the organization overview and "New organization" on the Organizations page send the description
// of the project (424-78, spec section 7), against the mock API of the e2e mode.

const server = setupServer(...mockHandlers)
// A failed test must not leave its teleported dialog in the body for the next one.
let unmount: (() => void) | undefined
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  unmount?.()
  unmount = undefined
  document.body.innerHTML = ''
  server.events.removeAllListeners()
  server.resetHandlers()
})
afterAll(() => server.close())

/** The bodies of the access points created from now on. */
function recordCreations() {
  const bodies: AccessPointCreate[] = []
  server.events.on('request:start', async ({ request }) => {
    if (request.method === 'POST' && new URL(request.url).pathname.endsWith('/access/points')) {
      bodies.push((await request.clone().json()) as AccessPointCreate)
    }
  })
  return bodies
}

async function openDialog(dialog: Component, props: Record<string, unknown> = {}) {
  const open = ref(false)
  const Harness = defineComponent({
    setup: () => () =>
      h(dialog, { ...props, open: open.value, 'onUpdate:open': (value: boolean) => (open.value = value) }),
  })
  const screen = await mountScreen(
    Harness,
    {},
    { path: '/orgs', accessMap: { acme: 'Owner', 'acme.billing': 'Owner' } },
  )
  unmount = () => screen.host.unmount()
  open.value = true
  await settle(6)
  return { open }
}

const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')!

async function fill(field: HTMLInputElement | HTMLTextAreaElement, value: string) {
  field.value = value
  field.dispatchEvent(new Event('input'))
  await settle()
}

function submit(label: string) {
  const button = [...dialog().querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)
  button!.click()
}

describe('the description of a new project', () => {
  it('"New project" sends it trimmed', async () => {
    const bodies = recordCreations()
    const { open } = await openDialog(ProjectCreateDialog, { org: 'acme' })

    await fill(dialog().querySelector('input')!, 'returns')
    await fill(dialog().querySelector('textarea')!, '  Returns of Acme.  ')
    submit('Create project')
    await vi.waitFor(() => expect(open.value).toBe(false))

    expect(bodies).toEqual([{ Organization: 'acme', Project: 'returns', ProjectDescription: 'Returns of Acme.' }])
    expect(loadDb().accessPoints['acme.returns']?.Description).toBe('Returns of Acme.')
  })

  it('"New project" leaves out a blank one', async () => {
    const bodies = recordCreations()
    const { open } = await openDialog(ProjectCreateDialog, { org: 'acme' })

    await fill(dialog().querySelector('input')!, 'returns')
    await fill(dialog().querySelector('textarea')!, '   ')
    submit('Create project')
    await vi.waitFor(() => expect(open.value).toBe(false))

    expect(bodies).toEqual([{ Organization: 'acme', Project: 'returns' }])
    expect(loadDb().accessPoints['acme.returns']).not.toHaveProperty('Description')
  })

  it('"New organization" sends the description of its first project trimmed', async () => {
    const bodies = recordCreations()
    const { open } = await openDialog(OrganizationCreateDialog)

    const [organization, project] = dialog().querySelectorAll('input')
    await fill(organization!, 'globex')
    await fill(project!, 'portal')
    await fill(dialog().querySelector('textarea')!, '  The customer portal of Globex.  ')
    submit('Create organization')
    await vi.waitFor(() => expect(open.value).toBe(false))

    expect(bodies).toEqual([
      { Organization: 'globex', Project: 'portal', ProjectDescription: 'The customer portal of Globex.' },
    ])
    expect(loadDb().accessPoints['globex.portal']?.Description).toBe('The customer portal of Globex.')
    expect(loadDb().accessPoints.globex).not.toHaveProperty('Description')
  })
})
