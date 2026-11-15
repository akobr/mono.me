import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { SetupContext } from 'vue'
import { RouterView } from 'vue-router'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import type { AccessAuditEntry, AccessAuditResponse } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed } from '@/mock/db'
import { mountScreen, settle, type AppMountOptions } from '../../helpers'

// The organization's Audit page (424-263): the records of the mock's seeded trail of acme, the filters as request
// parameters, Load more with the continuation token, and no request below the organization's Administrator.

// the navbar needs the dashboard shell; the stand-in shows its title
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
const mounted: { host: { unmount: () => void } }[] = []
/** The access audit requests of a test, in their order. */
const requests: URL[] = []
server.events.on('request:start', ({ request }) => {
  const url = new URL(request.url)
  if (url.pathname.endsWith('/audit')) {
    requests.push(url)
  }
})
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  requests.length = 0
  document.body.innerHTML = ''
})
afterAll(() => server.close())

// Ada owns acme and acme/billing, contributes to acme/shipping and reads northwind/portal.
const accessMap = createSeed('default').account!.AccessMap as Record<string, string>

async function mountAudit(map: Record<string, string> = accessMap) {
  const view = await mountScreen(
    RouterView,
    {},
    // an organization page: no project or view in the context
    { path: '/acme/audit', scope: { org: 'acme' } as AppMountOptions['scope'], accessMap: map, routes },
  )
  mounted.push(view)
  await settle(6)
  return view
}

const text = () => document.body.textContent ?? ''
const rows = () => [...document.body.querySelectorAll('tbody tr')] as HTMLTableRowElement[]
const rowWith = (content: string) => rows().find((row) => row.textContent?.includes(content))
const button = (name: string, root: ParentNode = document.body) =>
  [...root.querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === name || candidate.getAttribute('aria-label') === name,
  )
const input = (label: string) => document.body.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!

type View = Awaited<ReturnType<typeof mountAudit>>

/** The filter select with the label (USelect passes its aria-label on to the trigger). */
const selectOf = (view: View, label: string) =>
  view.host
    .findAllComponents({ name: 'Select' })
    .find((candidate) => (candidate.vm.$attrs as Record<string, unknown>)['aria-label'] === label)

/** Picks a value of a filter select, as choosing its option does. */
async function choose(view: View, label: string, value: string) {
  const select = selectOf(view, label)
  expect(select, label).toBeTruthy()
  select!.vm.$emit('update:modelValue', value)
  await settle()
}

async function type(label: string, value: string) {
  const field = input(label)
  field.value = value
  field.dispatchEvent(new Event('input'))
  await settle()
}

const entry = (id: string, extra: Partial<AccessAuditEntry> = {}): AccessAuditEntry => ({
  Id: id,
  Time: '2026-10-01T10:00:00Z',
  Action: 'MemberRoleChanged',
  AccessPointKey: 'acme.billing',
  Actor: { Kind: 'Account', Id: 'user_01MOCKADA', Name: 'Ada Lovelace' },
  Target: { Kind: 'Account', Id: `user_${id}`, Name: `Member ${id}` },
  OldValue: 'Reader',
  NewValue: 'Contributor',
  CorrelationId: `${id}-0000-4000-a000-000000000000`,
  ...extra,
})

describe('organization Audit page', () => {
  it('lists the records of the organization and its projects for its Owner, newest first', async () => {
    await mountAudit()

    await vi.waitFor(() => expect(rows().length).toBeGreaterThan(15))
    expect(text()).toContain(`${rows().length} record(s)`)
    expect(requests.map((url) => [url.pathname, url.search])).toEqual([['/api/v1/access/points/acme/audit', '']])

    // newest first: the federation rules of two days ago
    expect(rows()[0]!.textContent).toContain('Federation rules changed')
    expect(rows()[0]!.textContent).toContain('billing')
    expect(rows()[0]!.textContent).toContain('mach-release-pipeline')
    // the organization's own records, a removed member with its name, a person through MCP, DbCreator's System
    expect(rowWith('Charles Babbage')?.textContent).toContain('Organization')
    expect(rowWith('Member removed')?.textContent).toContain('user_01MOCKCHARLES')
    expect(rowWith('Role changed')?.textContent).toContain('Ada Lovelace via mcp')
    expect(rowWith('Alan Turing')).toBeTruthy()
    expect(rowWith('System')?.textContent).toContain('Project created')
    expect(rowWith('Organization created')).toBeTruthy()
    // the first 8 characters of the correlation id, with its copy button
    const policy = rowWith('Secrets policy changed')!
    expect(policy.textContent).toContain('00000000…')
    expect(button('Copy to clipboard', policy)).toBeTruthy()
    // personal data is kept out of PostHog
    expect(document.body.querySelector('table')?.classList).toContain('ph-no-capture')
  })

  it('shows a change of roles as badges, and JSON compactly with its full values on demand', async () => {
    await mountAudit()
    await vi.waitFor(() => expect(rowWith('Machine authentication changed')).toBeTruthy())

    const role = rowWith('Role changed')!
    expect(role.textContent).toMatch(/Contributor\s*to\s*Reader/)
    expect(button('Show the full values', role)).toBeUndefined()

    const policy = rowWith('Machine authentication changed')!
    const values = [...policy.querySelectorAll('code')].map((code) => code.textContent)
    expect(values).toEqual([
      '{"EnabledCredentialKinds":["ApiKey"],"CredentialKind":"ApiKey","CertificateLifetimeDays":90}',
      '{"EnabledCredentialKinds":["ApiKey","Federated"],"CredentialKind":"ApiKey","CertificateLifetimeDays":90}',
    ])
    // cut to one line, the full value in the title
    expect(policy.querySelector('code')?.classList).toContain('truncate')
    button('Show the full values', policy)!.click()
    await vi.waitFor(() => expect(text()).toContain('"EnabledCredentialKinds": [\n    "ApiKey",\n    "Federated"\n  ]'))
    expect(text()).toContain('Before')
    expect(text()).toContain('After')
  })

  it('sends the filters with the request and shows the matching records', async () => {
    const view = await mountAudit()
    await vi.waitFor(() => expect(rows().length).toBeGreaterThan(15))

    await choose(view, 'Project filter', 'billing')
    await choose(view, 'Action filter', 'MemberRoleChanged')
    await type('Target filter', ' user_01MOCKALAN ')
    await type('From', '2026-01-01')
    await type('To', '2099-12-31')

    await vi.waitFor(() => expect(requests.at(-1)?.searchParams.get('target')).toBe('user_01MOCKALAN'))
    const params = Object.fromEntries(requests.at(-1)!.searchParams)
    expect(params).toEqual({
      project: 'billing',
      action: 'MemberRoleChanged',
      target: 'user_01MOCKALAN',
      from: '2026-01-01T00:00:00.000Z',
      // the end date is inclusive
      to: '2100-01-01T00:00:00.000Z',
    })
    await vi.waitFor(() => expect(rows()).toHaveLength(1))
    expect(rows()[0]!.textContent).toContain('Alan Turing')
    expect(text()).toContain('1 record(s)')
  })

  it('offers the projects of the caller and of the records in the project filter', async () => {
    const view = await mountAudit()
    await vi.waitFor(() => expect(rows().length).toBeGreaterThan(15))

    const select = selectOf(view, 'Project filter')!
    expect((select.props('items') as { value: string }[]).map((item) => item.value)).toEqual([
      'all',
      'billing',
      'shipping',
    ])
  })

  it('appends the next page with Load more, with the same filters, and stops after the last page', async () => {
    const pages: Record<string, AccessAuditResponse> = {
      first: { Items: [entry('a'), entry('b')], ContinuationToken: 'next', Count: 2 },
      next: { Items: [entry('c')], Count: 1 },
    }
    server.use(
      http.get('*/api/v1/access/points/:key/audit', ({ request }) =>
        HttpResponse.json(pages[new URL(request.url).searchParams.get('continuationToken') ?? 'first']),
      ),
    )
    const view = await mountAudit()
    await vi.waitFor(() => expect(rows()).toHaveLength(2))
    await choose(view, 'Action filter', 'MemberRoleChanged')
    await vi.waitFor(() => expect(requests.at(-1)?.searchParams.get('action')).toBe('MemberRoleChanged'))
    await vi.waitFor(() => expect(button('Load more')).toBeTruthy())

    button('Load more')!.click()

    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    expect(rows().map((row) => row.querySelector('td:nth-child(4)')?.textContent?.trim())).toEqual([
      expect.stringContaining('Member a'),
      expect.stringContaining('Member b'),
      expect.stringContaining('Member c'),
    ])
    expect(Object.fromEntries(requests.at(-1)!.searchParams)).toEqual({
      action: 'MemberRoleChanged',
      continuationToken: 'next',
    })
    expect(button('Load more')).toBeUndefined()
    expect(text()).toContain('3 record(s)')
  })

  it('drops a next page that arrives after the filters changed', async () => {
    let release: () => void = () => {}
    const held = new Promise<void>((resolve) => (release = resolve))
    server.use(
      http.get('*/api/v1/access/points/:key/audit', async ({ request }) => {
        const url = new URL(request.url)
        if (url.searchParams.get('continuationToken')) {
          await held
          return HttpResponse.json({ Items: [entry('late')], Count: 1 })
        }
        const action = url.searchParams.get('action')
        return HttpResponse.json(
          action
            ? { Items: [entry('filtered', { Action: 'MemberRemoved' })], Count: 1 }
            : { Items: [entry('a')], ContinuationToken: 'next', Count: 1 },
        )
      }),
    )
    const view = await mountAudit()
    await vi.waitFor(() => expect(button('Load more')).toBeTruthy())

    button('Load more')!.click()
    await settle()
    await choose(view, 'Action filter', 'MemberRemoved')
    await vi.waitFor(() => expect(rowWith('Member filtered')).toBeTruthy())
    release()
    await settle(6)

    expect(rows()).toHaveLength(1)
    expect(rowWith('Member late')).toBeUndefined()
  })

  it('names the retention when there are no records', async () => {
    server.use(http.get('*/api/v1/access/points/:key/audit', () => HttpResponse.json({ Items: [], Count: 0 })))
    await mountAudit()

    await vi.waitFor(() => expect(text()).toContain('No audit records'))
    expect(text()).toContain('kept for two years')
    expect(document.body.querySelector('table')).toBeNull()
  })

  it('shows a failed load with Retry', async () => {
    let fail = true
    server.use(
      http.get('*/api/v1/access/points/:key/audit', () =>
        fail
          ? HttpResponse.json(
              { Message: 'No Administrator access to acme.', ErrorCode: 'AccessDenied' },
              { status: 403 },
            )
          : HttpResponse.json({ Items: [entry('a')], Count: 1 }),
      ),
    )
    await mountAudit()
    await vi.waitFor(() => expect(button('Retry')).toBeTruthy())

    fail = false
    button('Retry')!.click()

    await vi.waitFor(() => expect(rows()).toHaveLength(1))
  })

  it.each([
    ['a Contributor of the organization', { acme: 'Contributor', 'acme.billing': 'Owner' }],
    ['a member of its projects only', { 'acme.billing': 'Owner', 'acme.shipping': 'Owner' }],
  ])('shows Administrators only to %s, without asking for the records', async (_, map) => {
    await mountAudit(map)

    expect(text()).toContain('Administrators only')
    expect(document.body.querySelector('table')).toBeNull()
    expect(requests).toEqual([])
  })

  it('reads the trail of an Administrator of the organization', async () => {
    await mountAudit({ acme: 'Administrator' })

    await vi.waitFor(() => expect(rows().length).toBeGreaterThan(0))
    expect(text()).not.toContain('Administrators only')
  })
})
