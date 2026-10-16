import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import {
  deleteAnnotation,
  getAnnotation,
  getTemplate,
  getViewExport,
  getViewExportArchive,
  getViewImport,
  isStorytellerError,
  startViewExport,
  startViewImport,
  type StartViewImportParams,
  type ViewExportCreate,
  type ViewImport,
  type ViewImportItem,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { parseArchiveName } from '@/mock/archive-names'
import { resetTransfers } from '@/mock/transfer-handlers'
import { createZip, readZip, textFile, type ZipFile } from '@/mock/zip'

// The export layout and the import semantics of the mock API that the UI and its e2e tests rely on (spec 1.3-1.15):
// file names, folders and manifest, include, Existing, Force, DryRun, the overlay, warnings and the summaries per kind.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  resetTransfers()
})
afterAll(() => server.close())

const org = 'acme'
const project = 'billing'

interface Manifest {
  ManifestVersion: number
  Organization: string
  Project: string
  View: string
  ExportedAt: string
  Include: string[]
  Annotations: { Key: string; File: string; Created: unknown; Modified: unknown }[]
  Schemas: {
    Kind: string
    AnnotationType: string | null
    AnnotationKey: string | null
    File: string | null
    Versions: unknown[]
  }[]
  Templates: {
    AnnotationType: string
    File: string | null
    Version: number | null
    Versions: { Version: number; File: string }[]
  }[]
  Configurations: { AnnotationKey: string; File: string | null; Version: number | null; Versions: unknown[] }[]
}

const text = (file: ZipFile) => new TextDecoder().decode(file.data)
const json = (value: unknown) => `${JSON.stringify(value, null, 2)}\n`

/** Runs an export of a view to its end and returns its state and files. */
async function exportView(view = 'default', body: ViewExportCreate = { Format: 'Folder' }) {
  const started = await startViewExport(org, project, view, body)
  await getViewExport(org, project, view, started.Id!)
  const state = await getViewExport(org, project, view, started.Id!)
  const archive = await getViewExportArchive(org, project, view, started.Id!)
  const files = await readZip(new Uint8Array(await archive.arrayBuffer()))
  const manifest = files.find((file) => file.name === 'export.json')
  return { state, files, manifest: manifest && (JSON.parse(text(manifest)) as Manifest) }
}

/** Runs an import of the files into a view to its end (the second read of a mock job ends it). */
async function importFiles(files: ZipFile[], params: StartViewImportParams = {}, view = 'default') {
  const started = await startViewImport(org, project, view, new Blob([createZip(files)]), params)
  await getViewImport(org, project, view, started.Id!)
  return await getViewImport(org, project, view, started.Id!)
}

const item = (state: ViewImport, path: string) => state.Items?.find((entry) => entry.Path === path)
const summaryOf = (state: ViewImport, content: string) => state.Summaries?.find((entry) => entry.Content === content)

async function exists(key: string, view = 'default') {
  return getAnnotation(org, project, view, key).then(
    () => true,
    (error: unknown) => {
      if (isStorytellerError(error) && error.status === 404) {
        return false
      }
      throw error
    },
  )
}

describe('mock export', () => {
  it('writes every kind into its folder, with version files and the manifest version 2', async () => {
    const { state, files, manifest } = await exportView()
    const names = files.map((file) => file.name)

    expect(state).toMatchObject({ Status: 'Succeeded', Format: 'Folder' })
    expect(state.FileName).toMatch(/^export-acme-billing-default-\d{8}-\d{6}Z\.zip$/)
    expect(names.at(-1)).toBe('export.json')
    expect(
      names
        .filter((name) => name !== 'export.json')
        .every((name) => /^(annotations|schemas|templates|configurations)\//.test(name)),
    ).toBe(true)
    expect(names).toEqual(
      expect.arrayContaining([
        'annotations/rst.invoicing.json',
        'annotations/unt.invoicing.pdf.json',
        'schemas/exe.schema.json',
        'schemas/exe.schema.2.json',
        'schemas/sbt.northwind.exe.schema.json',
        'templates/exe.template.json',
        'templates/exe.template.1.json',
        'templates/exe.template.2.json',
      ]),
    )
    // every file is a document of the kind of its folder
    for (const name of names.filter((entry) => entry !== 'export.json')) {
      const [folder, fileName] = name.split('/')
      const parsed = parseArchiveName(fileName!)!
      expect(`${parsed.kind.replace(/^.*Schema$/, 'Schema').toLowerCase()}s`, name).toBe(folder)
    }

    // an annotation file holds the editable members only; a unit also its type and definition
    const annotation = JSON.parse(text(files.find((file) => file.name === 'annotations/rst.invoicing.json')!))
    expect(annotation.Title).toBe('Invoicing responsibility')
    expect(Object.keys(annotation)).not.toContain('AnnotationKey')
    expect(
      Object.keys(annotation).every((member) =>
        [
          'Title',
          'Description',
          'DocumentationLink',
          'Labels',
          'Values',
          'IsDisabled',
          'ValidFrom',
          'ExpiresAt',
          'TimeZone',
        ].includes(member),
      ),
    ).toBe(true)
    expect(JSON.parse(text(files.find((file) => file.name === 'annotations/unt.invoicing.pdf.json')!))).toMatchObject({
      UnitType: 'event',
      UnitDefinition: 'unknown',
    })

    expect(manifest).toMatchObject({
      ManifestVersion: 2,
      Organization: 'acme',
      Project: 'billing',
      View: 'default',
      Include: ['Annotations', 'Schemas', 'Templates', 'Configurations'],
    })
    expect(manifest!.Annotations.find((entry) => entry.Key === 'rst.invoicing')).toEqual({
      Key: 'rst.invoicing',
      File: 'annotations/rst.invoicing.json',
      Created: { At: expect.any(String), By: { Kind: 'System' } },
      Modified: { At: expect.any(String), By: { Kind: 'System' } },
    })
    expect(manifest!.Templates.find((entry) => entry.AnnotationType === 'exe')).toMatchObject({
      File: 'templates/exe.template.json',
      Version: 3,
      Versions: [
        { Version: 1, File: 'templates/exe.template.1.json' },
        { Version: 2, File: 'templates/exe.template.2.json' },
      ],
    })
    expect(manifest!.Schemas.find((entry) => entry.Kind === 'DescendantTypeSchema')).toMatchObject({
      AnnotationType: 'exe',
      AnnotationKey: 'sbt.northwind',
      File: 'schemas/sbt.northwind.exe.schema.json',
    })
    expect(
      manifest!.Configurations.every((entry) => entry.File === `configurations/${entry.AnnotationKey}.conf.json`),
    ).toBe(true)

    // the counts of the job
    const versions = [...manifest!.Schemas, ...manifest!.Templates, ...manifest!.Configurations].reduce(
      (sum, entry) => sum + (entry.File ? 1 : 0) + entry.Versions.length,
      0,
    )
    expect(state).toMatchObject({
      AnnotationCount: manifest!.Annotations.length,
      SchemaCount: manifest!.Schemas.length,
      TemplateCount: 2,
      ConfigurationCount: manifest!.Configurations.length,
      VersionCount: versions,
      Size: expect.any(Number),
    })
    expect(names.length).toBe(1 + manifest!.Annotations.length + versions)
  })

  it('writes only the included kinds', async () => {
    const { state, files, manifest } = await exportView('default', {
      Format: 'Folder',
      Include: ['Annotations', 'Templates'],
    })

    expect(new Set(files.map((file) => file.name.split('/')[0]))).toEqual(
      new Set(['annotations', 'templates', 'export.json']),
    )
    expect(manifest).toMatchObject({ Include: ['Annotations', 'Templates'], Schemas: [], Configurations: [] })
    expect(state.AnnotationCount).toBeGreaterThan(0)
    expect(state.TemplateCount).toBe(2)
    expect(state).not.toHaveProperty('SchemaCount')
    expect(state).not.toHaveProperty('ConfigurationCount')
    expect(state.VersionCount).toBe(6)
  })

  it('writes a git export as the current documents and .git/HEAD', async () => {
    const { state, files, manifest } = await exportView('default', { Format: 'Git' })
    const names = files.map((file) => file.name)

    expect(state.FileName).toMatch(/^export-acme-billing-default-git-\d{8}-\d{6}Z\.zip$/)
    expect(names).toContain('.git/HEAD')
    expect(manifest).toBeUndefined()
    expect(names).toContain('templates/exe.template.json')
    expect(
      names
        .filter((name) => !name.startsWith('.git/'))
        .every((name) => parseArchiveName(name.split('/')[1]!)?.version === undefined),
    ).toBe(true)
  })
})

describe('mock import', () => {
  it('imports an export back unchanged, with a summary per kind and the version files ignored', async () => {
    const { files, manifest } = await exportView()
    const versionFiles = files.filter((file) => parseArchiveName(file.name.split('/').at(-1)!)?.version !== undefined)
    const documents = files.length - versionFiles.length - 1

    const state = await importFiles(files)

    expect(state).toMatchObject({
      Status: 'Succeeded',
      Include: ['Annotations', 'Schemas', 'Templates', 'Configurations'],
      Existing: 'Overwrite',
      TotalCount: documents,
      ProcessedCount: documents,
      Items: [],
      AreItemsTruncated: false,
    })
    expect(state.Summary).toEqual({
      Created: 0,
      Updated: 0,
      Unchanged: documents,
      Skipped: 0,
      Failed: 0,
      Warnings: 0,
      ExcludedFiles: 0,
      IgnoredFiles: versionFiles.length + 1,
    })
    expect(state.Summaries?.map((summary) => [summary.Content, summary.Unchanged])).toEqual([
      ['Annotations', manifest!.Annotations.length],
      ['Schemas', manifest!.Schemas.length],
      ['Templates', 2],
      ['Configurations', manifest!.Configurations.length],
    ])
  })

  it('reads the files by their names in any folder and skips hidden paths without counting them', async () => {
    const { files } = await exportView('default', { Format: 'Folder', Include: ['Templates', 'Schemas'] })
    const flat = files.map((file) => ({ ...file, name: `elsewhere/${file.name.split('/').at(-1)}` }))

    const state = await importFiles([
      ...flat,
      textFile('.git/HEAD', 'ref: refs/heads/main\n'),
      textFile('.github/rst.hidden.json', '{}'),
      textFile('__MACOSX/annotations/rst.mac.json', '{}'),
      textFile('annotations/._rst.mac.json', '{}'),
      textFile('notes.txt', 'ignored'),
    ])

    expect(state.Summary).toMatchObject({ Created: 0, Updated: 0, Failed: 0, Unchanged: state.TotalCount })
    // export.json, the version files and notes.txt; the hidden paths are not counted
    expect(state.Summary?.IgnoredFiles).toBe(files.length - state.TotalCount! + 1)
  })

  it('copies a view into another one: the dry run predicts the real run, which a second run leaves unchanged', async () => {
    // every schema and template, and the subject stark, which the staging view lacks
    const files = (await exportView()).files.filter(
      (file) => !/^(annotations|configurations)\//.test(file.name) || file.name.includes('.stark.'),
    )
    const outcomes = (state: ViewImport) =>
      state.Items!.map((entry) => [
        entry.Path,
        entry.Outcome,
        entry.ErrorCode,
        entry.Warnings?.map((warning) => warning.Code),
      ])

    const checked = await importFiles(files, { dryRun: true }, 'staging')
    const imported = await importFiles(files, {}, 'staging')
    const again = await importFiles(files, {}, 'staging')

    expect(checked.IsDryRun).toBe(true)
    expect(checked.Summaries).toEqual(imported.Summaries)
    expect(outcomes(checked)).toEqual(outcomes(imported))
    expect(imported.Summary).toMatchObject({ Failed: 0 })
    expect(again.Summary).toMatchObject({ Created: 0, Updated: 0, Failed: 0 })
    expect(summaryOf(imported, 'Annotations')!.Created).toBeGreaterThan(0)
    expect(summaryOf(imported, 'Configurations')!.Created).toBeGreaterThan(0)
    // written documents carry their version, a dry run none
    const configuration = imported.Items!.find((entry) => entry.Kind === 'Configuration' && entry.Outcome === 'Created')
    expect(configuration?.Version).toBeGreaterThan(0)
    expect(checked.Items!.every((entry) => entry.Version === undefined)).toBe(true)
  })

  it('creates missing ancestors with defaults and warns; a dry run writes nothing', async () => {
    const files = [textFile('annotations/exe.newco.billing.prod.json', json({ Title: 'New execution' }))]
    const warning = {
      Code: 'AncestorCreated',
      Message:
        'The missing ancestors rst.billing, sbt.newco, usg.newco.billing, cnt.newco.prod are created with defaults.',
    }

    const checked = await importFiles(files, { dryRun: true })
    expect(await exists('exe.newco.billing.prod')).toBe(false)

    const imported = await importFiles(files)

    for (const state of [checked, imported]) {
      expect(state.Items).toEqual([
        {
          Path: 'annotations/exe.newco.billing.prod.json',
          Kind: 'Annotation',
          AnnotationKey: 'exe.newco.billing.prod',
          Outcome: 'Created',
          Warnings: [warning],
        },
      ])
      expect(summaryOf(state, 'Annotations')).toMatchObject({ Created: 1, Warnings: 1 })
      expect(state.Summary).toMatchObject({ Created: 1, Warnings: 1 })
    }
    expect((await getAnnotation(org, project, 'default', 'exe.newco.billing.prod')).Title).toBe('New execution')
    expect(await exists('cnt.newco.prod')).toBe(true)
  })

  it('keeps every kind the view has with Skip, and still writes what is new', async () => {
    const { manifest } = await exportView('default', { Format: 'Folder', Include: ['Configurations'] })
    const key = manifest!.Configurations[0]!.AnnotationKey
    const files = [
      textFile('annotations/rst.invoicing.json', json({ Title: 'Edited' })),
      textFile('annotations/rst.billing.json', json({ Title: 'New' })),
      textFile('schemas/cnt.schema.json', json({ type: 'object' })),
      textFile('templates/exe.template.json', json({ retries: 9 })),
      textFile(`configurations/${key}.conf.json`, json({ editedByTest: true })),
    ]

    const state = await importFiles(files, { existing: 'Skip' })

    expect(state.Existing).toBe('Skip')
    expect(state.Items!.map((entry) => [entry.Path, entry.Outcome, entry.ErrorCode, entry.Message])).toEqual([
      ['annotations/rst.billing.json', 'Created', undefined, undefined],
      [
        'annotations/rst.invoicing.json',
        'Skipped',
        'DocumentExists',
        'The view has a different annotation rst.invoicing, and Skip keeps it.',
      ],
      [
        'schemas/cnt.schema.json',
        'Skipped',
        'DocumentExists',
        'The view has a different type schema cnt, and Skip keeps it.',
      ],
      [
        'templates/exe.template.json',
        'Skipped',
        'DocumentExists',
        'The view has a different template exe, and Skip keeps it.',
      ],
      [
        `configurations/${key}.conf.json`,
        'Skipped',
        'DocumentExists',
        `The view has a different configuration of ${key}, and Skip keeps it.`,
      ],
    ])
    expect((await getTemplate(org, project, 'default', 'exe')).Content).not.toHaveProperty('retries', 9)
  })

  it('overwrites with a new version and counts the kinds left out as excluded', async () => {
    const files = [
      textFile('annotations/rst.invoicing.json', json({ Title: 'Edited' })),
      textFile('templates/exe.template.json', json({ retries: 9 })),
      textFile('configurations/rst.invoicing.conf.json', json({ a: 1 })),
    ]

    const state = await importFiles(files, { include: ['Templates'] })

    expect(state.Include).toEqual(['Templates'])
    expect(state.Summaries).toEqual([
      {
        Content: 'Templates',
        Created: 0,
        Updated: 1,
        Unchanged: 0,
        Skipped: 0,
        Failed: 0,
        Warnings: 0,
        ExcludedFiles: 0,
        IgnoredFiles: 0,
      },
    ])
    expect(state.Summary).toMatchObject({ Updated: 1, ExcludedFiles: 2, IgnoredFiles: 0 })
    expect(state.Items).toEqual([
      { Path: 'templates/exe.template.json', Kind: 'Template', AnnotationType: 'exe', Outcome: 'Updated', Version: 4 },
    ])
    expect((await getTemplate(org, project, 'default', 'exe')).Content).toEqual({ retries: 9 })
    expect((await getAnnotation(org, project, 'default', 'rst.invoicing')).Title).not.toBe('Edited')
  })

  it('checks a schema against the configurations the archive brings; force stores a violation with a warning', async () => {
    const { files } = await exportView('default', { Format: 'Folder', Include: ['Configurations'] })
    const contexts = files.filter((file) => /^configurations\/cnt\.[^.]+\.[^.]+\.conf\.json$/.test(file.name))
    expect(contexts.length).toBeGreaterThan(1)
    const keys = contexts.map((file) => parseArchiveName(file.name.split('/')[1]!)!.key!).toSorted()
    const schema = textFile('schemas/cnt.schema.json', json({ type: 'object', required: ['owner'] }))
    const withOwner = contexts.map((file) => textFile(file.name, json({ ...JSON.parse(text(file)), owner: 'ops' })))

    const alone = await importFiles([schema], { dryRun: true })
    const forced = await importFiles([schema], { dryRun: true, force: true })
    const together = await importFiles([schema, ...withOwner])

    expect(item(alone, 'schemas/cnt.schema.json')).toMatchObject({
      Kind: 'TypeSchema',
      AnnotationType: 'cnt',
      Outcome: 'Failed',
      ErrorCode: 'SchemaViolation',
      Message: `The configurations of ${keys.join(', ')} violate this schema.`,
    })
    expect(item(forced, 'schemas/cnt.schema.json')).toMatchObject({
      Outcome: 'Updated',
      Warnings: [
        {
          Code: 'SchemaViolation',
          Message: `Force stores this schema although the configurations of ${keys.join(', ')} violate it.`,
        },
      ],
    })
    expect(summaryOf(forced, 'Schemas')).toMatchObject({ Updated: 1, Warnings: 1 })
    expect(together.Summary).toMatchObject({ Updated: 1 + contexts.length, Failed: 0 })

    // a configuration that breaks the stored schema fails without force and is stored with a warning with it
    const broken = [textFile(`configurations/${keys[0]}.conf.json`, json({ owner: 'ops' }))]
    expect(item(await importFiles(broken), broken[0]!.name)).toMatchObject({ Outcome: 'Updated' })
    const violating = [textFile(`configurations/${keys[0]}.conf.json`, json({ maintenance: 'often' }))]
    expect(item(await importFiles(violating), violating[0]!.name)).toMatchObject({
      Outcome: 'Failed',
      ErrorCode: 'SchemaViolation',
    })
    expect(item(await importFiles(violating, { force: true }), violating[0]!.name)).toMatchObject({
      Outcome: 'Updated',
      Warnings: [expect.objectContaining({ Code: 'SchemaViolation' })],
    })
  })

  it('skips what hangs on a failed annotation with DependencyFailed', async () => {
    const files = [
      textFile('annotations/rst.billing.json', json({ Title: 'Billing', Colour: 'red' })),
      textFile('annotations/unt.billing.pdf.json', json({})),
      textFile('schemas/rst.billing.schema.json', json({ type: 'object' })),
      textFile('configurations/rst.billing.conf.json', json({ a: 1 })),
    ]

    const state = await importFiles(files)

    expect(state.Items!.map((entry) => [entry.Kind, entry.Outcome, entry.ErrorCode, entry.Message])).toEqual([
      ['Annotation', 'Failed', 'InvalidDocument', "Unknown member 'Colour'."],
      ['Annotation', 'Skipped', 'DependencyFailed', 'The ancestor rst.billing failed in this import.'],
      ['AnnotationSchema', 'Skipped', 'DependencyFailed', 'The annotation rst.billing failed in this import.'],
      ['Configuration', 'Skipped', 'DependencyFailed', 'The annotation rst.billing failed in this import.'],
    ])
    expect(await exists('unt.billing.pdf')).toBe(false)
  })

  it('re-creates a deleted annotation with Overwrite and leaves it deleted with Skip', async () => {
    await deleteAnnotation(org, project, 'default', 'rst.archiving')
    const files = [
      textFile('annotations/rst.archiving.json', json({ Title: 'Back again' })),
      textFile('configurations/rst.archiving.conf.json', json({ retention: 30 })),
    ]

    const skipped = await importFiles(files, { existing: 'Skip' })
    const recreated = await importFiles(files)

    expect(skipped.Items!.map((entry) => [entry.Outcome, entry.ErrorCode, entry.Message])).toEqual([
      ['Skipped', 'AnnotationDeleted', 'The annotation rst.archiving is deleted, and Skip leaves it deleted.'],
      [
        'Skipped',
        'AnnotationDeleted',
        'The annotation rst.archiving is deleted; restore it before importing its configuration.',
      ],
    ])
    expect(recreated.Items).toEqual([
      expect.objectContaining({
        Kind: 'Annotation',
        Outcome: 'Created',
        Warnings: [expect.objectContaining({ Code: 'AnnotationRecreated' })],
      }),
      expect.objectContaining({ Kind: 'Configuration', Outcome: 'Created', Version: expect.any(Number) }),
    ])
    expect((await getAnnotation(org, project, 'default', 'rst.archiving')).Title).toBe('Back again')
  })

  it('reports duplicates, broken files, invalid schemas and misplaced descendant schemas', async () => {
    const files = [
      textFile('annotations/rst.invoicing.json', json({ Title: 'Invoicing responsibility' })),
      textFile('copy/rst.invoic%69ng.json', json({ Title: 'Other' })),
      textFile('rst.broken.conf.json', '{"a":'),
      textFile('schemas/exe.schema.json', json({ type: 42 })),
      textFile('schemas/exe.s.r.c.rst.schema.json', json({ type: 'object' })),
      textFile('templates/uxe.template.json', '// a comment\n{ "retries": 2, }\n'),
      textFile('annotations/rst.Invoicing.json', json({ AnnotationKey: 'rst.other' })),
    ]

    const state = await importFiles(files, { dryRun: true })
    const byPath = (path: string): ViewImportItem | undefined => item(state, path)

    expect(state.TotalCount).toBe(files.length)
    expect(byPath('copy/rst.invoic%69ng.json')).toMatchObject({
      Outcome: 'Failed',
      ErrorCode: 'DuplicateFile',
      Message: "The file 'annotations/rst.invoicing.json' already holds the annotation rst.invoicing.",
    })
    expect(byPath('rst.broken.conf.json')).toMatchObject({ Outcome: 'Failed', ErrorCode: 'InvalidDocument' })
    expect(byPath('schemas/exe.schema.json')).toMatchObject({ Outcome: 'Failed', ErrorCode: 'InvalidSchema' })
    expect(byPath('schemas/exe.s.r.c.rst.schema.json')).toMatchObject({
      Kind: 'DescendantTypeSchema',
      AnnotationType: 'rst',
      AnnotationKey: 'exe.s.r.c',
      Outcome: 'Failed',
      ErrorCode: 'InvalidKey',
    })
    expect(byPath('templates/uxe.template.json')).toMatchObject({ Outcome: 'Updated' })
    expect(byPath('annotations/rst.Invoicing.json')).toMatchObject({
      Outcome: 'Failed',
      ErrorCode: 'InvalidDocument',
      Message: 'The AnnotationKey "rst.other" is not rst.Invoicing, the key of the file name.',
    })
    // the first file of rst.invoicing wins; it replaces the content, so the members it lacks are cleared
    expect(byPath('annotations/rst.invoicing.json')).toMatchObject({ Outcome: 'Updated' })
    expect(state.Summary).toMatchObject({ Unchanged: 0, Updated: 2, Failed: 5 })
  })

  it('refuses an annotation that breaks the annotation rules', async () => {
    const state = await importFiles([textFile('annotations/rst.long.json', json({ Title: 'x'.repeat(201) }))])

    expect(state.Items).toEqual([
      expect.objectContaining({
        Outcome: 'Failed',
        ErrorCode: 'InvalidAnnotation',
        Message: "The annotation 'rst.long' is not valid: Title has 201 characters, at most 200 are allowed.",
      }),
    ])
  })
})
