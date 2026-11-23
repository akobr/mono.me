import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { http } from 'msw'
import { setupServer } from 'msw/node'
import { getAnnotation, setAnnotation, type Annotation } from '@42for.net/2splatform.sdk'
import AnnotationForm from '@/components/annotations/AnnotationForm.vue'
import { toAnnotationDraft } from '@/forms/annotation.schema'
import { mockHandlers } from '@/mock/all-handlers'
import { mountScreen, settle } from '../../helpers'

// Saving the Annotation tab (424-225) sends only the members the user changed, as a PATCH: what someone else changed
// since the form loaded stays, and the form shows the merged result.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const [org, project, view] = ['acme', 'billing', 'default']
const keyUrl = '*/api/v1/:org/:project/:view/annotations/:key'

const read = (key: string) => getAnnotation(org, project, view, key, { includeInactive: true })

function recordWrites(): { method: string; body: unknown }[] {
  const writes: { method: string; body: unknown }[] = []
  const record = async ({ request }: { request: Request }) => {
    writes.push({ method: request.method, body: await request.clone().json() })
  }
  server.use(http.patch(keyUrl, record), http.post(keyUrl, record))
  return writes
}

async function mountForm(annotation: Annotation, path?: string) {
  const screen = await mountScreen(AnnotationForm, { annotation, fresh: true }, { path })
  await settle()
  return screen
}

/** The values editor emits the whole record, as a row edit does. */
async function editValues(screen: Awaited<ReturnType<typeof mountForm>>, values: Record<string, unknown>) {
  screen.host.findComponent({ name: 'ValuesEditor' }).vm.$emit('update:modelValue', values)
  await settle()
}

/** Writes sbt.northwind whole with a known state and returns it as read. */
async function seed(changes: Partial<Annotation>) {
  const loaded = await read('sbt.northwind')
  await setAnnotation(org, project, view, 'sbt.northwind', { ...loaded, ...changes })
  return read('sbt.northwind')
}

const titleInput = (screen: Awaited<ReturnType<typeof mountForm>>) =>
  screen.host.find<HTMLInputElement>('input:not([type="url"])')

/** Clicks Save and waits until the form is clean again: saved, or reset to what it loaded. */
async function save(screen: Awaited<ReturnType<typeof mountForm>>) {
  const button = screen.host.findAll('button').find((candidate) => candidate.text().startsWith('Save'))!
  expect(screen.host.text()).toContain('Unsaved changes')
  await button.trigger('click')
  await vi.waitFor(() => expect(screen.host.text()).not.toContain('Unsaved changes'), { timeout: 5000 })
  await settle()
}

describe('AnnotationForm save', () => {
  it('patches only the changed title and keeps a value written elsewhere after the form loaded', async () => {
    const loaded = await read('sbt.northwind')
    await setAnnotation(org, project, view, 'sbt.northwind', { ...loaded, Values: { owner: 'ops' } })
    const writes = recordWrites()
    const screen = await mountForm(loaded)

    await titleInput(screen).setValue('Northwind Traders')
    await save(screen)

    expect(writes).toEqual([{ method: 'PATCH', body: { Title: 'Northwind Traders' } }])
    expect(await read('sbt.northwind')).toMatchObject({ Title: 'Northwind Traders', Values: { owner: 'ops' } })
    // the form took the merged result: nothing unsaved, the value written elsewhere is shown
    expect(screen.host.text()).not.toContain('Unsaved changes')
    expect(screen.host.html()).toContain('owner')
    screen.host.unmount()
  })

  it('clears an emptied member with null', async () => {
    const loaded = await read('sbt.northwind')
    await setAnnotation(org, project, view, 'sbt.northwind', { ...loaded, Title: 'Northwind' })
    const writes = recordWrites()
    const screen = await mountForm(await read('sbt.northwind'))

    await titleInput(screen).setValue('')
    await save(screen)

    expect(writes).toEqual([{ method: 'PATCH', body: { Title: null } }])
    expect(await read('sbt.northwind')).not.toHaveProperty('Title')
    screen.host.unmount()
  })

  it('sends nothing when the change trims away', async () => {
    const loaded = await read('sbt.northwind')
    await setAnnotation(org, project, view, 'sbt.northwind', { ...loaded, Title: 'Northwind' })
    const writes = recordWrites()
    const screen = await mountForm(await read('sbt.northwind'))

    await titleInput(screen).setValue('Northwind  ')
    await save(screen)

    expect(writes).toEqual([])
    expect(screen.host.text()).not.toContain('Unsaved changes')
    screen.host.unmount()
  })
})

describe('AnnotationForm: what a save is the difference from', () => {
  it('forgets a removed value name after its save, so the next save does not bring it back', async () => {
    const loaded = await seed({ Title: 'Northwind', Values: { plan: 'basic', old: 1 } })
    const writes = recordWrites()
    const screen = await mountForm(loaded)

    await editValues(screen, { plan: 'basic' })
    await save(screen)
    await titleInput(screen).setValue('Northwind Traders')
    await save(screen)

    expect(writes.map((write) => write.body)).toEqual([{ Values: { old: null } }, { Title: 'Northwind Traders' }])
    expect((await read('sbt.northwind')).Values).toEqual({ plan: 'basic' })
    screen.host.unmount()
  })

  it('takes a fresh copy that arrives while the form is clean as its new base', async () => {
    const stale = await seed({ Title: 'Northwind', Values: { plan: 'basic', old: 1 } })
    const fresh = await seed({ Title: 'Renamed elsewhere', Values: { plan: 'basic' } })
    const writes = recordWrites()
    const screen = await mountForm(stale)

    await screen.setProps({ annotation: fresh })
    await editValues(screen, { plan: 'pro' })
    await save(screen)

    expect(writes.map((write) => write.body)).toEqual([{ Values: { plan: 'pro' } }])
    expect(await read('sbt.northwind')).toMatchObject({ Title: 'Renamed elsewhere', Values: { plan: 'pro' } })
    screen.host.unmount()
  })

  it('keeps its base while the form is dirty, so only the user changes go out', async () => {
    const stale = await seed({ Title: 'Northwind', Description: 'Old' })
    const fresh = await seed({ Title: 'Renamed elsewhere', Description: 'Old' })
    const writes = recordWrites()
    const screen = await mountForm(stale)

    await editValues(screen, { plan: 'pro' })
    await screen.setProps({ annotation: fresh })
    await save(screen)

    expect(writes.map((write) => write.body)).toEqual([{ Values: { plan: 'pro' } }])
    expect(await read('sbt.northwind')).toMatchObject({ Title: 'Renamed elsewhere', Values: { plan: 'pro' } })
    screen.host.unmount()
  })

  it('reverts to the latest copy, which becomes the base', async () => {
    const stale = await seed({ Title: 'Northwind', Values: { plan: 'basic', old: 1 } })
    const fresh = await seed({ Title: 'Renamed elsewhere', Values: { plan: 'basic' } })
    const writes = recordWrites()
    const screen = await mountForm(stale)

    await editValues(screen, { plan: 'pro', old: 1 })
    await screen.setProps({ annotation: fresh })
    await screen.host
      .findAll('button')
      .find((button) => button.text() === 'Revert')!
      .trigger('click')
    await settle()
    expect(titleInput(screen).element.value).toBe('Renamed elsewhere')

    await titleInput(screen).setValue('Northwind Traders')
    await save(screen)

    expect(writes.map((write) => write.body)).toEqual([{ Title: 'Northwind Traders' }])
    expect((await read('sbt.northwind')).Values).toEqual({ plan: 'basic' })
    screen.host.unmount()
  })

  it('sets a value to null with a JSON Patch', async () => {
    const recorded: (string | null)[] = []
    server.use(http.patch(keyUrl, ({ request }) => void recorded.push(request.headers.get('Content-Type'))))
    const loaded = await seed({ Values: { plan: 'basic' } })
    const writes = recordWrites()
    const screen = await mountForm(loaded)

    await editValues(screen, { plan: null })
    await save(screen)

    expect(recorded).toEqual(['application/json-patch+json'])
    expect(writes.map((write) => write.body)).toEqual([[{ op: 'add', path: '/Values/plan', value: null }]])
    expect((await read('sbt.northwind')).Values).toEqual({ plan: null })
    screen.host.unmount()
  })

  it('takes a value name and a member removed in the JSON mode as removed', async () => {
    const loaded = await seed({ Title: 'Northwind', Description: 'Old', Values: { plan: 'basic', old: 1 } })
    const writes = recordWrites()
    const screen = await mountForm(loaded, '/acme/billing/default/annotations/sbt.northwind?format=json')

    const { Description: _description, ...document } = { ...toAnnotationDraft(loaded), Values: { plan: 'basic' } }
    screen.host.findComponent({ name: 'DocumentEditor' }).vm.$emit('update:modelValue', document)
    await settle()
    await save(screen)

    expect(writes.map((write) => write.body)).toEqual([{ Description: null, Values: { old: null } }])
    expect(await read('sbt.northwind')).not.toHaveProperty('Description')
    screen.host.unmount()
  })

  it('applies only the members of the annotation from the JSON mode, never another name as a path', async () => {
    const loaded = await seed({ Title: 'Northwind', Values: { plan: 'basic' } })
    const writes = recordWrites()
    const screen = await mountForm(loaded, '/acme/billing/default/annotations/sbt.northwind?format=json')
    const editor = () => screen.host.findComponent({ name: 'DocumentEditor' })

    // a typo and a dotted name: neither is a member, and "Values.plan" must not reach Values.plan
    editor().vm.$emit('update:modelValue', { ...toAnnotationDraft(loaded), Titel: 'typo', 'Values.plan': 'pro' })
    await settle()
    expect(screen.host.text()).not.toContain('Unsaved changes')
    expect(editor().props('modelValue')).toMatchObject({ Values: { plan: 'basic' } })

    editor().vm.$emit('update:modelValue', { ...toAnnotationDraft(loaded), Title: 'Edited', 'Values.plan': 'pro' })
    await settle()
    await save(screen)

    expect(writes.map((write) => write.body)).toEqual([{ Title: 'Edited' }])
    expect((await read('sbt.northwind')).Values).toEqual({ plan: 'basic' })
    screen.host.unmount()
  })

  it('keeps a value named __proto__ when another member is saved', async () => {
    const loaded = await seed({ Values: JSON.parse('{"__proto__": {"kept": true}, "plan": "basic"}') })
    const writes = recordWrites()
    const screen = await mountForm(loaded)

    await titleInput(screen).setValue('Northwind Traders')
    await save(screen)

    expect(writes.map((write) => write.body)).toEqual([{ Title: 'Northwind Traders' }])
    expect(Object.keys((await read('sbt.northwind')).Values ?? {}).toSorted()).toEqual(['__proto__', 'plan'])
    screen.host.unmount()
  })
})
