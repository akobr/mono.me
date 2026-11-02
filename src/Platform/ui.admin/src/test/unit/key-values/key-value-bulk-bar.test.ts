import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import KeyValueBulkBar from '@/components/key-values/KeyValueBulkBar.vue'
import { mountScreen, settle } from '../../helpers'

// The API takes at most 100 keys per bulk call (MaxBulkKeys); a larger selection goes in batches (424-159).
const batches: string[][] = []
const server = setupServer(
  http.post('*/api/v1/:org/:project/:view/key-values/restore', async ({ request }) => {
    const keys = ((await request.json()) as { Keys: string[] }).Keys
    batches.push(keys)
    if (batches.length === 2) {
      return HttpResponse.json({ Message: 'boom', Code: 'Failed' }, { status: 500 })
    }
    return HttpResponse.json({ Items: keys.map((key) => ({ Key: key, Outcome: 'Done' })) })
  }),
)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => (document.body.innerHTML = ''))
afterAll(() => server.close())

describe('KeyValueBulkBar', () => {
  it('restores in batches of 100 and keeps the keys of a failed batch', async () => {
    const keys = Array.from({ length: 250 }, (_, index) => `key.${index}`)
    const done = vi.fn()
    const view = await mountScreen(KeyValueBulkBar, { keys, state: 'Deleted', allowedLevels: ['Public'], onDone: done })

    ;[...document.body.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Restore')!.click()
    await vi.waitFor(() => expect(done).toHaveBeenCalled())
    await settle()

    expect(batches.map((batch) => batch.length)).toEqual([100, 100, 50])
    expect(done).toHaveBeenCalledWith(keys.slice(100, 200))
    view.host.unmount()
  })
})
