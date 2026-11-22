import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
import { http } from 'msw'
import { setupServer } from 'msw/node'
import { deleteAnnotation, getAnnotation, setAnnotation, type Annotation } from '@42for.net/2splatform.sdk'
import { useAnnotationActions } from '@/composables/useAnnotationActions'
import { mockHandlers } from '@/mock/all-handlers'
import { useCatalogStore } from '@/stores/catalog'
import { mountScreen, settle } from '../../helpers'

// The bulk edits of the annotation list (424-225) write only their change with PATCH, against the stateful mock API:
// a change someone else made after the catalog loaded is kept, and a key deleted meanwhile is not re-created.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }
const { org, project, view } = scope
const keyUrl = '*/api/v1/:org/:project/:view/annotations/:key'

interface Recorded {
  method: string
  key: string
  contentType: string | null
  body: unknown
}

/** Every write to an annotation's own route; returning nothing lets the mock answer it. */
function recordWrites(): Recorded[] {
  const writes: Recorded[] = []
  const record = async ({ request, params }: { request: Request; params: Record<string, unknown> }) => {
    writes.push({
      method: request.method,
      key: String(params.key),
      contentType: request.headers.get('Content-Type'),
      body: await request.clone().json(),
    })
  }
  server.use(http.patch(keyUrl, record), http.post(keyUrl, record))
  return writes
}

const read = (key: string) => getAnnotation(org, project, view, key, { includeInactive: true })

/** Someone else writes the annotation whole, after the catalog loaded. */
async function writeElsewhere(key: string, changes: Partial<Annotation>) {
  await setAnnotation(org, project, view, key, { ...(await read(key)), ...changes })
}

const Harness = defineComponent({
  setup(_, { expose }) {
    expose({ actions: useAnnotationActions() })
    return () => null
  },
})

type Actions = ReturnType<typeof useAnnotationActions>

/** Mounts the actions over a loaded catalog of the view, after `prepare` set the stored state. */
async function actionsOver(prepare: () => Promise<void> = async () => {}) {
  await prepare()
  await useCatalogStore().load(scope)
  const screen = await mountScreen(Harness, {})
  return {
    actions: (screen.inner().vm as unknown as { actions: Actions }).actions,
    unmount: () => screen.host.unmount(),
  }
}

const entry = (key: string) => useCatalogStore().byKey.get(key)

describe('useAnnotationActions: bulk edits as PATCH', () => {
  it('disables and enables with a merge patch of IsDisabled alone, keeping a concurrent title', async () => {
    const { actions, unmount } = await actionsOver()
    await writeElsewhere('sbt.northwind', { Title: 'Renamed elsewhere' })
    const writes = recordWrites()

    await actions.setDisabled(['sbt.northwind', 'sbt.contoso'], true)

    expect(writes.map(({ method, key, contentType, body }) => [method, key, contentType, body]).toSorted()).toEqual([
      ['PATCH', 'sbt.contoso', 'application/merge-patch+json', { IsDisabled: true }],
      ['PATCH', 'sbt.northwind', 'application/merge-patch+json', { IsDisabled: true }],
    ])
    expect(await read('sbt.northwind')).toMatchObject({ IsDisabled: true, Title: 'Renamed elsewhere' })
    expect(entry('sbt.northwind')).toMatchObject({ IsDisabled: true, Title: 'Renamed elsewhere' })

    await actions.setDisabled(['sbt.northwind'], false)

    expect(writes.at(-1)).toMatchObject({ method: 'PATCH', body: { IsDisabled: null } })
    expect(await read('sbt.northwind')).not.toHaveProperty('IsDisabled')
    expect(actions.progress.value).toBeUndefined()
    unmount()
  })

  it('adds a label with add /Labels/- guarded by the labels read, and skips a key that has it in any case', async () => {
    const { actions, unmount } = await actionsOver(async () => {
      await writeElsewhere('sbt.northwind', { Labels: ['eu'] })
      await writeElsewhere('sbt.contoso', { Labels: ['Beta'] })
    })
    const writes = recordWrites()

    await actions.addLabel(['sbt.northwind', 'sbt.contoso'], ' beta ')

    expect(writes).toEqual([
      {
        method: 'PATCH',
        key: 'sbt.northwind',
        contentType: 'application/json-patch+json',
        body: [
          { op: 'test', path: '/Labels', value: ['eu'] },
          { op: 'add', path: '/Labels/-', value: 'beta' },
        ],
      },
    ])
    expect((await read('sbt.northwind')).Labels).toEqual(['eu', 'beta'])
    expect(entry('sbt.northwind')!.Labels).toEqual(['eu', 'beta'])
    expect((await read('sbt.contoso')).Labels).toEqual(['Beta'])
    unmount()
  })

  it('reads the labels again when they changed meanwhile, and keeps the concurrent label', async () => {
    const { actions, unmount } = await actionsOver(async () => {
      await writeElsewhere('sbt.northwind', { Labels: ['eu'] })
    })
    await writeElsewhere('sbt.northwind', { Labels: ['eu', 'gdpr'] })
    const writes = recordWrites()

    await actions.addLabel(['sbt.northwind'], 'beta')

    expect(writes.map((write) => write.body)).toEqual([
      [
        { op: 'test', path: '/Labels', value: ['eu'] },
        { op: 'add', path: '/Labels/-', value: 'beta' },
      ],
      [
        { op: 'test', path: '/Labels', value: ['eu', 'gdpr'] },
        { op: 'add', path: '/Labels/-', value: 'beta' },
      ],
    ])
    expect((await read('sbt.northwind')).Labels).toEqual(['eu', 'gdpr', 'beta'])
    expect(entry('sbt.northwind')!.Labels).toEqual(['eu', 'gdpr', 'beta'])
    unmount()
  })

  it('adds no second spelling of a label someone added meanwhile', async () => {
    const { actions, unmount } = await actionsOver(async () => {
      await writeElsewhere('sbt.northwind', { Labels: ['eu'] })
    })
    await writeElsewhere('sbt.northwind', { Labels: ['eu', 'BETA'] })
    const writes = recordWrites()

    await actions.addLabel(['sbt.northwind'], 'beta')

    expect(writes).toHaveLength(1)
    expect((await read('sbt.northwind')).Labels).toEqual(['eu', 'BETA'])
    expect(entry('sbt.northwind')!.Labels).toEqual(['eu', 'BETA'])
    unmount()
  })

  it('removes every spelling of a label with guarded removes', async () => {
    const { actions, unmount } = await actionsOver(async () => {
      await writeElsewhere('sbt.northwind', { Labels: ['pci', 'eu', 'PCI'] })
    })
    const writes = recordWrites()

    await actions.removeLabel(['sbt.northwind', 'sbt.contoso'], 'Pci')

    expect(writes.filter((write) => write.key === 'sbt.northwind').map((write) => write.body)).toEqual([
      [
        { op: 'test', path: '/Labels', value: ['pci', 'eu', 'PCI'] },
        { op: 'remove', path: '/Labels/2' },
        { op: 'remove', path: '/Labels/0' },
      ],
    ])
    expect((await read('sbt.northwind')).Labels).toEqual(['eu'])
    unmount()
  })

  it('reads the labels again when they moved meanwhile, and removes the right one', async () => {
    const { actions, unmount } = await actionsOver(async () => {
      await writeElsewhere('sbt.northwind', { Labels: ['eu', 'pci'] })
    })
    await writeElsewhere('sbt.northwind', { Labels: ['gdpr', 'eu', 'pci'] })
    const writes = recordWrites()

    await actions.removeLabel(['sbt.northwind'], 'pci')

    expect(writes.map((write) => write.body)).toEqual([
      [
        { op: 'test', path: '/Labels', value: ['eu', 'pci'] },
        { op: 'remove', path: '/Labels/1' },
      ],
      [
        { op: 'test', path: '/Labels', value: ['gdpr', 'eu', 'pci'] },
        { op: 'remove', path: '/Labels/2' },
      ],
    ])
    expect((await read('sbt.northwind')).Labels).toEqual(['gdpr', 'eu'])
    expect(entry('sbt.northwind')!.Labels).toEqual(['gdpr', 'eu'])
    unmount()
  })

  it('takes a fresh copy when the label was removed meanwhile', async () => {
    const { actions, unmount } = await actionsOver(async () => {
      await writeElsewhere('sbt.northwind', { Labels: ['eu', 'pci'] })
    })
    await writeElsewhere('sbt.northwind', { Labels: ['eu'], Title: 'Renamed elsewhere' })
    const writes = recordWrites()

    await actions.removeLabel(['sbt.northwind'], 'pci')

    expect(writes).toHaveLength(1)
    expect(entry('sbt.northwind')).toMatchObject({ Labels: ['eu'], Title: 'Renamed elsewhere' })
    unmount()
  })

  it('fails a key deleted meanwhile and leaves it deleted, while the others are written', async () => {
    const { actions, unmount } = await actionsOver()
    await deleteAnnotation(org, project, view, 'sbt.contoso')
    const writes = recordWrites()

    await expect(actions.setDisabled(['sbt.northwind', 'sbt.contoso'], true)).rejects.toThrow(
      '1 of 2 annotations were not saved: sbt.contoso',
    )

    expect(writes.every((write) => write.method === 'PATCH')).toBe(true)
    expect(await read('sbt.northwind')).toMatchObject({ IsDisabled: true })
    await expect(read('sbt.contoso')).rejects.toMatchObject({ status: 404 })
    // a delete would have taken it from the catalog: the bulk action does too
    expect(entry('sbt.contoso')).toBeUndefined()
    expect(actions.progress.value).toBeUndefined()
    await settle()
    unmount()
  })
})

describe('useAnnotationActions().patch', () => {
  it('merges on the server, puts the stored result into the catalog and returns it', async () => {
    const { actions, unmount } = await actionsOver()
    await writeElsewhere('sbt.northwind', { Values: { owner: 'ops' } })

    const saved = await actions.patch('sbt.northwind', { Title: 'Patched', Values: { plan: 'pro' } })

    expect(saved).toMatchObject({ Title: 'Patched', Values: { owner: 'ops', plan: 'pro' } })
    expect(entry('sbt.northwind')).toMatchObject({ Title: 'Patched', Values: { owner: 'ops', plan: 'pro' } })
    unmount()
  })
})
