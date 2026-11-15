// @vitest-environment node
import { readdirSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it, vi } from 'vitest'
import { getPath } from '@/mock/archive-names'
import type { DocumentId } from '@/mock/documents'
import { keyPattern } from '@/mock/key-values'
import { buildShowcaseScope } from '@/mock/showcase'
import { showcaseKeyValues } from '@/mock/showcase/key-values'
import { annotationDocument } from '@/mock/transfer-handlers'

// The built-in data set showcase of DbCreator (424-252) is the fabrikam / invoicing-suite / default scope of the
// showcase, written in the export's folder format: an annotation file per active annotation and the current version of
// every document, as a Git export of the view gives them, and the current value of every pair that is not deleted in
// key-values.json, as the key/value export gives them (424-265). The files are snapshots, so `npm test` fails when the
// mock and the set part (CI writes no snapshot); regenerate them with `npx vitest run test/unit/mock/showcase-set.test.ts -u`
// (the path first: -u takes a value) and delete the files the test reports as stale. Only set.json is written by hand.

const [org, project, view] = ['fabrikam', 'invoicing-suite', 'default']
const setFolder = fileURLToPath(
  new URL('../../../../../Storyteller/DbCreator/src/data-sets/showcase/', import.meta.url),
)
// a fixed clock, so the dates the scope computes from now (ValidFrom, ExpiresAt) stay the same
const clock = new Date('2026-11-01T00:00:00Z')

const json = (value: unknown) => `${JSON.stringify(value, null, 2)}\n`
const hasContent = (value: unknown) => typeof value === 'object' && value !== null && Object.keys(value).length > 0

/** The path of a document of the mock in an export, and the annotation it hangs on (none for a type's documents). */
function documentPath(id: DocumentId): { path: string; owner?: string } {
  if (id.startsWith('configuration:')) {
    const key = id.slice('configuration:'.length)
    return { path: getPath('Configuration', undefined, key), owner: key }
  }
  if (id.startsWith('template:')) {
    return { path: getPath('Template', id.slice('template:'.length), undefined) }
  }
  if (id.startsWith('schema:type:')) {
    return { path: getPath('TypeSchema', id.slice('schema:type:'.length), undefined) }
  }
  if (id.startsWith('schema:annotation:')) {
    const key = id.slice('schema:annotation:'.length)
    return { path: getPath('AnnotationSchema', undefined, key), owner: key }
  }
  const rest = id.slice('schema:descendant:'.length)
  const separator = rest.lastIndexOf(':')
  const key = rest.slice(0, separator)
  return { path: getPath('DescendantTypeSchema', rest.slice(separator + 1), key), owner: key }
}

/** The pairs of the scope as the key/value export lists them: the current value of every pair that is not deleted, by key. */
function keyValuesDocument() {
  const items = showcaseKeyValues(`${org}/${project}/${view}`)
    .filter((stored) => stored.Deleted === undefined)
    .map((stored) => {
      const current = stored.Versions.at(-1)!
      return {
        Key: stored.Key,
        Protection: current.Protection,
        Value: current.Value,
        ...(current.ContentType === undefined ? {} : { ContentType: current.ContentType }),
      }
    })
    .toSorted((left, right) => (left.Key < right.Key ? -1 : left.Key > right.Key ? 1 : 0))
  return { Format: '2s.key-values.v1', Items: items }
}

/** The files of the set by path, as an export of the view writes them. */
function setFiles(): Map<string, string> {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(clock)
  try {
    const scope = buildShowcaseScope(org, project, view)!
    const active = new Set(scope.annotations.map((annotation) => annotation.AnnotationKey))
    const files = new Map<string, string>()
    for (const annotation of scope.annotations) {
      files.set(getPath('Annotation', undefined, annotation.AnnotationKey), json(annotationDocument(annotation)))
    }
    // like the export: no document of a deleted annotation, no deleted document
    for (const [id, document] of scope.documents) {
      const { path, owner } = documentPath(id)
      const current = document.versions.at(-1)?.Content
      if ((owner === undefined || active.has(owner)) && hasContent(current)) {
        files.set(path, json(current))
      }
    }
    files.set('key-values.json', json(keyValuesDocument()))
    return files
  } finally {
    vi.useRealTimers()
  }
}

/** The files below the set folder with '/' paths, hidden ones left out as the import skips them. */
function filesOnDisk(): string[] {
  return readdirSync(setFolder, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => relative(setFolder, join(entry.parentPath, entry.name)).replaceAll('\\', '/'))
    .filter((path) => !path.split('/').some((segment) => segment.startsWith('.')))
    .toSorted()
}

describe('the data set showcase of DbCreator', () => {
  const files = setFiles()

  it('holds the annotations and the documents of fabrikam / invoicing-suite / default', () => {
    const paths = [...files.keys()]
    expect(paths.filter((path) => path.startsWith('annotations/')).length).toBeGreaterThan(150)
    expect(paths).toEqual(
      expect.arrayContaining([
        'annotations/rst.invoicing.json',
        'configurations/rst.invoicing.conf.json',
        'schemas/rst.invoicing.usg.schema.json',
        'templates/uxe.template.json',
      ]),
    )
  })

  it('holds pairs a seed can write: Public and Standard, with valid keys and a value each', () => {
    const { Items: items } = keyValuesDocument()
    expect(items.map((item) => item.Protection)).toEqual(expect.arrayContaining(['Public', 'Standard']))
    for (const item of items) {
      expect(item.Key, item.Key).toMatch(keyPattern)
      expect(['Public', 'Standard'], item.Key).toContain(item.Protection)
      expect(item.Value, item.Key).not.toBe('')
    }
    // the deleted pair of the mock stays out of the set
    expect(items.map((item) => item.Key)).not.toContain('legacy.ftpHost')
  })

  it.each([...files.keys()].toSorted())('writes %s', async (path) => {
    await expect(files.get(path)).toMatchFileSnapshot(`${setFolder}${path}`)
  })

  it('holds no file the scope no longer gives', () => {
    const stale = filesOnDisk().filter((path) => path !== 'set.json' && !files.has(path))
    expect(stale, 'delete the stale files of the set').toEqual([])
  })
})
