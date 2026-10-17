import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import type { VueWrapper } from '@vue/test-utils'
import { setupServer } from 'msw/node'
import { getViewImport, startViewExport, startViewImport, type ViewImport } from '@42for.net/2splatform.sdk'
import ExportPanel from '@/components/transfer/ExportPanel.vue'
import ImportPanel from '@/components/transfer/ImportPanel.vue'
import ImportResult from '@/components/transfer/ImportResult.vue'
import TransferToaster from '@/components/transfer/TransferToaster.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { resetTransfers } from '@/mock/transfer-handlers'
import { createZip, textFile } from '@/mock/zip'
import ExportImportPage from '@/pages/[org]/[project]/[view]/export-import.vue'
import { useTransfersStore } from '@/stores/transfers'
import { saveBlob } from '@/utils/download'
import { mountScreen, settle } from '../../helpers'

// The sections of the Export & Import page and the toaster (spec 4.2-4.5 of the Export and Import of a View), against
// the mock API of the e2e mode.

vi.mock('@/utils/download', () => ({ saveBlob: vi.fn() }))
// the navbar needs the dashboard shell; the page is tested without it
vi.mock('@/components/shell/AppNavbar.vue', () => ({ default: { name: 'AppNavbar', render: () => null } }))

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  server.events.removeAllListeners()
  resetTransfers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }

const buttons = (root: ParentNode = document.body) => [...root.querySelectorAll<HTMLButtonElement>('button')]
const button = (label: string, root?: ParentNode) =>
  buttons(root).find((candidate) => candidate.textContent?.trim() === label)
const checkbox = (label: string, root: ParentNode = document.body) => {
  const id = [...root.querySelectorAll('label')].find((candidate) => candidate.textContent?.trim() === label)?.htmlFor
  return root.querySelector<HTMLButtonElement>(`[id="${id}"]`)!
}

async function click(element: HTMLElement | undefined) {
  expect(element).toBeDefined()
  element!.click()
  await settle()
}

/** The bodies or query strings of the requests to start a transfer. */
function recordStarts() {
  const starts: { url: URL; body?: unknown }[] = []
  server.events.on('request:start', async ({ request }) => {
    if (request.method === 'POST') {
      const url = new URL(request.url)
      starts.push({ url, body: url.pathname.endsWith('/exports') ? await request.clone().json() : undefined })
    }
  })
  return starts
}

describe('ExportPanel', () => {
  it('exports the checked kinds in import order', async () => {
    const starts = recordStarts()
    const view = await mountScreen(ExportPanel, { scope })
    const root = view.inner().element as HTMLElement

    expect(
      ['Annotations', 'Schemas', 'Templates', 'Configurations'].map((kind) => checkbox(kind, root).dataset.state),
    ).toEqual(['checked', 'checked', 'checked', 'checked'])
    expect(root.textContent).toContain('Never included: secrets, effective documents, the audit')

    await click(checkbox('Schemas', root))
    await click(checkbox('Configurations', root))
    await click(button('Export', root))
    await vi.waitFor(() => expect(starts).toHaveLength(1))

    expect(starts[0]!.body).toEqual({ Format: 'Folder', Include: ['Annotations', 'Templates'] })
    expect(useTransfersStore().latestExport(scope)?.state.Include).toEqual(['Annotations', 'Templates'])
  })

  it('needs at least one kind', async () => {
    const view = await mountScreen(ExportPanel, { scope })
    const root = view.inner().element as HTMLElement

    for (const kind of ['Annotations', 'Schemas', 'Templates', 'Configurations']) {
      await click(checkbox(kind, root))
    }

    expect(root.textContent).toContain('Pick at least one kind.')
    expect(button('Export', root)!.disabled).toBe(true)
  })

  it('shows an export that the store follows again after a reload', async () => {
    vi.mocked(saveBlob).mockClear()
    // before the reload: an export of the view that was still running
    const { Id: id } = await startViewExport(scope.org, scope.project, scope.view, { Format: 'Folder' })
    sessionStorage.setItem('2s-admin:pending-exports', JSON.stringify([{ id, scope, startedAt: Date.now() }]))

    // after the reload: the section is set up before TransferToaster resumes the pending exports once it is mounted
    const view = await mountScreen(ExportPanel, { scope })
    const root = view.inner().element as HTMLElement
    expect(button('Export', root)!.disabled).toBe(false)
    useTransfersStore().resume()
    await settle()

    expect(root.textContent).toContain('The download starts when it is ready')
    expect(button('Export', root)!.disabled).toBe(true)
    await vi.waitFor(() => expect(root.textContent).toContain('was downloaded.'), { timeout: 10_000 })
    expect(vi.mocked(saveBlob)).toHaveBeenCalledOnce()
    expect(button('Export', root)!.disabled).toBe(false)
  })
})

const archive = () =>
  new File([createZip([textFile('annotations/rst.invoicing.json', '{"Title":"Edited"}')])], 'edited.zip', {
    type: 'application/zip',
  })

describe('ImportPanel', () => {
  async function withFile(view: { inner: () => VueWrapper }) {
    view.inner().findComponent({ name: 'FileUpload' }).vm.$emit('update:modelValue', archive())
    await settle()
  }

  it('tells a reader that importing needs the Contributor role', async () => {
    const view = await mountScreen(ImportPanel, { scope }, { accessMap: { 'acme.billing': 'Reader' } })
    const root = view.inner().element as HTMLElement

    expect(root.textContent).toContain('Importing needs the Contributor role')
    expect(root.querySelector('input[type="file"]')).toBeNull()
    expect(button('Check', root)).toBeUndefined()
  })

  it('checks a zip with the chosen options, and imports it now with the same ones', async () => {
    const starts = recordStarts()
    const view = await mountScreen(ImportPanel, { scope })
    const root = view.inner().element as HTMLElement
    expect(button('Check', root)!.disabled).toBe(true)
    expect(root.textContent).toContain(
      "Folders don't matter: files are recognized by their names. Version files, export.json and hidden folders are ignored.",
    )

    await withFile(view)
    await click(checkbox('Configurations', root))
    await click(root.querySelector<HTMLElement>('[role="radio"][value="Skip"]')!)
    await click(checkbox('Store schemas and configurations that break each other', root))
    await click(button('Check', root))
    await vi.waitFor(() => expect(starts).toHaveLength(1))

    expect(Object.fromEntries(starts[0]!.url.searchParams)).toEqual({
      include: 'Annotations,Schemas,Templates',
      existing: 'Skip',
      force: 'true',
      dryRun: 'true',
      fileName: 'edited.zip',
    })

    const transfers = useTransfersStore()
    const checked = transfers.latestImport(scope)!
    await vi.waitFor(() => expect(checked.phase).toBe('done'), { timeout: 10_000 })
    ;(view.inner().vm as unknown as { importNow: () => void }).importNow()
    await vi.waitFor(() => expect(starts).toHaveLength(2))

    expect(Object.fromEntries(starts[1]!.url.searchParams)).toEqual({
      include: 'Annotations,Schemas,Templates',
      existing: 'Skip',
      force: 'true',
      fileName: 'edited.zip',
    })
  })
})

describe('ImportResult', () => {
  const result: ViewImport = {
    Id: 'job1',
    FileName: 'edited.zip',
    IsDryRun: true,
    Include: ['Annotations', 'Schemas', 'Templates', 'Configurations'],
    Summary: {
      Created: 1,
      Updated: 2,
      Unchanged: 5,
      Skipped: 1,
      Failed: 1,
      Warnings: 1,
      ExcludedFiles: 2,
      IgnoredFiles: 3,
    },
    Summaries: [
      { Content: 'Annotations', Created: 1, Updated: 1, Unchanged: 2, Skipped: 0, Failed: 0, Warnings: 1 },
      { Content: 'Schemas', Created: 0, Updated: 0, Unchanged: 1, Skipped: 0, Failed: 1, Warnings: 0 },
      { Content: 'Templates', Created: 0, Updated: 1, Unchanged: 0, Skipped: 0, Failed: 0, Warnings: 0 },
      { Content: 'Configurations', Created: 0, Updated: 0, Unchanged: 2, Skipped: 1, Failed: 0, Warnings: 0 },
    ],
    Items: [
      {
        Path: 'annotations/exe.newco.billing.prod.json',
        Kind: 'Annotation',
        AnnotationKey: 'exe.newco.billing.prod',
        Outcome: 'Created',
        Warnings: [{ Code: 'AncestorCreated', Message: 'The missing ancestors sbt.newco are created with defaults.' }],
      },
      {
        Path: 'annotations/rst.invoicing.json',
        Kind: 'Annotation',
        AnnotationKey: 'rst.invoicing',
        Outcome: 'Updated',
      },
      {
        Path: 'schemas/sbt.northwind.exe.schema.json',
        Kind: 'DescendantTypeSchema',
        AnnotationKey: 'sbt.northwind',
        AnnotationType: 'exe',
        Outcome: 'Failed',
        ErrorCode: 'InvalidSchema',
        Message: 'Not a valid JSON Schema.',
      },
      { Path: 'templates/exe.template.json', Kind: 'Template', AnnotationType: 'exe', Outcome: 'Updated', Version: 4 },
      {
        Path: 'configurations/rst.invoicing.conf.json',
        Kind: 'Configuration',
        AnnotationKey: 'rst.invoicing',
        Outcome: 'Skipped',
        ErrorCode: 'DocumentExists',
        Message: 'The view has a different configuration of rst.invoicing, and Skip keeps it.',
      },
    ],
  }

  const rows = (root: Element) => [...root.querySelectorAll('table[aria-label="Summary"] tbody tr')]
  const documents = (root: Element) => [...root.querySelectorAll('table:not([aria-label]) tbody tr')]

  it('shows a banner, the counts per kind and the total, and Import now after a check', async () => {
    const view = await mountScreen(ImportResult, { result, scope, canImportNow: true })
    const root = view.inner().element as HTMLElement

    expect(root.textContent).toContain('Checked: nothing was written')
    expect(root.textContent).toContain('edited.zip: 1 to create, 2 to update, 5 unchanged, 1 skipped, 1 failed.')
    const cells = (row: Element) => [...row.children].map((cell) => cell.textContent?.trim()).filter(Boolean)
    expect(rows(root).map((row) => cells(row).join(' '))).toEqual([
      'Annotations 1 1 2 0 0 1',
      'Schemas 0 0 1 0 1 0',
      'Templates 0 1 0 0 0 0',
      'Configurations 0 0 2 1 0 0',
      'Total 1 2 5 1 1 1 2 3',
    ])

    await click(button('Import now', root))
    expect(view.inner().emitted('importNow')).toHaveLength(1)
  })

  it('lists the documents with their kind, outcome, code, message, warnings and links', async () => {
    const view = await mountScreen(ImportResult, { result: { ...result, IsDryRun: false }, scope })
    const root = view.inner().element as HTMLElement
    const texts = documents(root).map((row) => row.textContent?.replace(/\s+/g, ' ').trim())

    expect(root.textContent).toContain('Imported')
    expect(button('Import now', root)).toBeUndefined()
    expect(texts[0]).toContain('Annotation')
    expect(texts[0]).toContain('AncestorCreated The missing ancestors sbt.newco are created with defaults.')
    expect(texts[2]).toContain('Descendant-type schema')
    expect(texts[2]).toContain('exe below sbt.northwind')
    expect(texts[2]).toContain('InvalidSchema')
    expect(texts[3]).toContain('v4')
    expect([...root.querySelectorAll('table:not([aria-label]) a')].map((link) => link.getAttribute('href'))).toEqual([
      '/acme/billing/default/annotations/exe.newco.billing.prod',
      '/acme/billing/default/annotations/rst.invoicing',
      '/acme/billing/default/templates/exe',
      // kept by Skip, so the view has it
      '/acme/billing/default/configurations/rst.invoicing',
    ])
  })

  it('filters the documents by kind and by outcome', async () => {
    const view = await mountScreen(ImportResult, { result, scope })
    const root = view.inner().element as HTMLElement
    const keys = () => documents(root).map((row) => row.querySelector('td:nth-child(2) p')?.textContent?.trim())

    expect(
      buttons(root)
        .filter((candidate) => candidate.hasAttribute('aria-pressed'))
        .map((chip) => chip.textContent?.trim()),
    ).toEqual(['Annotations 2', 'Schemas 1', 'Templates 1', 'Configurations 1', 'Fails 1', 'To skip 1', 'Warnings 1'])

    await click(button('Annotations 2', root))
    expect(keys()).toEqual(['annotations/exe.newco.billing.prod.json', 'annotations/rst.invoicing.json'])

    await click(button('Warnings 1', root))
    expect(keys()).toEqual(['annotations/exe.newco.billing.prod.json'])

    // kinds and outcomes narrow each other; outcomes add up
    await click(button('Annotations 2', root))
    await click(button('Fails 1', root))
    expect(keys()).toEqual(['annotations/exe.newco.billing.prod.json', 'schemas/sbt.northwind.exe.schema.json'])

    await click(button('Clear the filter', root))
    expect(keys()).toHaveLength(5)
  })
})

describe('TransferToaster', () => {
  const zip = () =>
    new File([createZip([textFile('templates/exe.template.json', '{"retries": 7}')])], 'template.zip', {
      type: 'application/zip',
    })

  it('reports an import that no page shows with a link to the page, and refreshes the kinds it wrote', async () => {
    const view = await mountScreen(TransferToaster, {})
    const invalidated = vi.spyOn(view.queryClient, 'invalidateQueries')
    const transfers = useTransfersStore()

    const job = await transfers.startImport(scope, zip())
    await vi.waitFor(() => expect(job.phase).toBe('done'), { timeout: 10_000 })
    await vi.waitFor(() => expect(document.body.textContent).toContain('Imported template.zip'))

    const link = [...document.body.querySelectorAll('a')].find((anchor) =>
      anchor.textContent?.includes('Show the result'),
    )
    expect(link?.getAttribute('href')).toBe('/acme/billing/default/export-import')
    const keys = invalidated.mock.calls.map(([filters]) =>
      (filters as { queryKey: string[] }).queryKey.slice(4).join('/'),
    )
    expect(new Set(keys)).toEqual(new Set(['template', 'templates', 'configuration', 'configurations']))
  })

  it('does not report an import that the page shows', async () => {
    await mountScreen(TransferToaster, {})
    const transfers = useTransfersStore()

    const job = await transfers.startImport(scope, zip(), { dryRun: true })
    const release = transfers.showInPage(job.id)
    await vi.waitFor(() => expect(job.phase).toBe('done'), { timeout: 10_000 })
    await settle()

    expect(document.body.textContent).not.toContain('Checked template.zip')
    release()
  })
})

describe('the Export & Import page', () => {
  it('shows an import that ended during a reload, and the toaster does not report it', async () => {
    const { org, project, view } = scope
    // before the reload: a check that the page followed; the mock ends a job on its second read
    const started = await startViewImport(org, project, view, archive(), { dryRun: true, fileName: 'edited.zip' })
    await getViewImport(org, project, view, started.Id!)
    sessionStorage.setItem(
      '2s-admin:latest-imports',
      JSON.stringify([{ id: started.Id, scope, startedAt: Date.now(), finished: false }]),
    )

    // after the reload: the page and the toaster are set up in one patch, and the toaster reads the import again
    const Reloaded = defineComponent({ setup: () => () => [h(ExportImportPage), h(TransferToaster)] })
    await mountScreen(Reloaded, {})
    const transfers = useTransfersStore()
    await vi.waitFor(() => expect(transfers.latestImport(scope)?.phase).toBe('done'))
    await settle()

    expect(transfers.watched.has(started.Id!)).toBe(true)
    expect(document.body.textContent).toContain('Checked: nothing was written')
    expect(document.body.textContent).not.toContain('Checked edited.zip')
  })
})
