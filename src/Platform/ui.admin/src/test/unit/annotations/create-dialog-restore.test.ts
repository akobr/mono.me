import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { http } from 'msw'
import { setupServer } from 'msw/node'
import { deleteAnnotation, getAnnotation, getAnnotationHistory, setAnnotation } from '@42for.net/2splatform.sdk'
import AnnotationCreateDialog from '@/components/annotations/AnnotationCreateDialog.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { mountScreen, settle } from '../../helpers'

// Restore and update of New annotation (424-225): the deleted key is restored with its content, and only what the
// dialog sets is patched over it; Create new stays a whole write (POST), the only one that re-creates a key.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const [org, project, view] = ['acme', 'billing', 'default']
const keyUrl = '*/api/v1/:org/:project/:view/annotations/:key'

function recordWrites(): { method: string; key: string; body: unknown }[] {
  const writes: { method: string; key: string; body: unknown }[] = []
  const record = async ({ request, params }: { request: Request; params: Record<string, unknown> }) => {
    writes.push({ method: request.method, key: String(params.key), body: await request.clone().json() })
  }
  server.use(http.patch(keyUrl, record), http.post(keyUrl, record))
  return writes
}

/** Opens New annotation for a subject and fills its name and title, then presses Create and answers the deleted keys. */
async function createSubject(name: string, title: string, answer: 'Restore and update' | 'Create new') {
  const screen = await mountScreen(AnnotationCreateDialog, { open: true }, { accessMap: { 'acme.billing': 'Owner' } })
  await settle()
  const type = screen.host.findAllComponents({ name: 'Select' })[0]!
  type.vm.$emit('update:modelValue', 'sbt')
  await settle()

  const inputs = () => [...document.body.querySelectorAll<HTMLInputElement>('[role="dialog"] input')]
  const [nameInput, titleInput] = inputs().filter((input) => input.getAttribute('role') !== 'combobox')
  for (const [input, value] of [
    [nameInput!, name],
    [titleInput!, title],
  ] as const) {
    input.value = value
    input.dispatchEvent(new Event('input'))
  }
  await settle()

  const button = (label: string) =>
    [...document.body.querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)
  button('Create')!.click()
  await vi.waitFor(() => expect(button(answer)).toBeDefined(), { timeout: 5000 })
  button(answer)!.click()
  await vi.waitFor(() => expect(screen.inner().emitted('created')).toBeDefined(), { timeout: 5000 })
  await settle()
  return screen
}

describe('New annotation over a deleted key', () => {
  it('Restore and update restores the key with its content and patches only the title', async () => {
    const current = await getAnnotation(org, project, view, 'sbt.contoso', { includeInactive: true })
    await setAnnotation(org, project, view, 'sbt.contoso', {
      ...current,
      Description: 'Kept through the delete',
      Labels: ['eu'],
      TimeZone: 'Asia/Tokyo',
    })
    await deleteAnnotation(org, project, view, 'sbt.contoso')
    const writes = recordWrites()

    const screen = await createSubject('contoso', ' Contoso Ltd ', 'Restore and update')

    expect(writes).toEqual([{ method: 'PATCH', key: 'sbt.contoso', body: { Title: 'Contoso Ltd' } }])
    expect(await getAnnotation(org, project, view, 'sbt.contoso')).toMatchObject({
      Title: 'Contoso Ltd',
      Description: 'Kept through the delete',
      Labels: ['eu'],
      TimeZone: 'Asia/Tokyo',
    })
    const actions = ((await getAnnotationHistory(org, project, view, 'sbt.contoso')).Entries ?? []).map(
      (entry) => entry.Action,
    )
    expect(actions.slice(0, 3)).toEqual(['Updated', 'Restored', 'Deleted'])
    expect(screen.inner().emitted('created')![0]).toEqual(['sbt.contoso'])
    screen.host.unmount()
  })

  it('Create new writes the key whole with POST', async () => {
    await deleteAnnotation(org, project, view, 'sbt.globex')
    const writes = recordWrites()

    const screen = await createSubject('globex', 'Globex', 'Create new')

    expect(writes.map(({ method, key }) => [method, key])).toEqual([['POST', 'sbt.globex']])
    expect(await getAnnotation(org, project, view, 'sbt.globex')).toMatchObject({ Title: 'Globex' })
    screen.host.unmount()
  })
})
