import { describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { editableOf, sameValue, toAnnotationPatch } from '@/forms/annotation-patch'
import { toAnnotationDraft, type AnnotationForm } from '@/forms/annotation.schema'
import { applyAnnotationPatch } from '@/mock/annotation-patch'
import { labelAddition, labelRemoval, patchableLabels, sameLabel } from '@/utils/labels'

// The PATCH body of the edit form (424-225): only what the user changed since the form was reset, a merge patch
// unless a value is set to JSON null.

const loaded: Annotation = {
  ProjectName: 'billing',
  ViewName: 'default',
  AnnotationKey: 'sbt.northwind',
  Name: 'northwind',
  AnnotationType: 'Subject',
  Title: 'Northwind',
  Description: 'Our oldest customer.',
  DocumentationLink: 'https://docs.example.com/northwind',
  TimeZone: 'Europe/Prague',
  ValidFrom: '2026-11-01T09:30:00+01:00',
  Labels: ['eu', 'pci'],
  Values: { plan: 'basic', retries: 3, limits: { cpu: 2, memory: 4 }, regions: ['eu'] },
}

const base = toAnnotationDraft(loaded)
const edit = (changes: Partial<AnnotationForm>): AnnotationForm => ({ ...toAnnotationDraft(loaded), ...changes })
const patchOf = (changes: Partial<AnnotationForm>) => toAnnotationPatch(edit(changes), base)

describe('toAnnotationPatch: a merge patch of the changed members', () => {
  it('is undefined when nothing changed, also after trimming and for the same instant', () => {
    expect(patchOf({})).toBeUndefined()
    expect(patchOf({ Title: '  Northwind  ', TimeZone: ' Europe/Prague' })).toBeUndefined()
    expect(patchOf({ ValidFrom: '2026-11-01T08:30:00.000Z' })).toBeUndefined()
    expect(patchOf({ IsDisabled: false })).toBeUndefined()
    expect(
      patchOf({ Values: { regions: ['eu'], limits: { memory: 4, cpu: 2 }, retries: 3, plan: 'basic' } }),
    ).toBeUndefined()
  })

  it('sends each changed member with its new value, an emptied one as null', () => {
    expect(patchOf({ Title: 'Northwind Traders' })).toEqual({ Title: 'Northwind Traders' })
    expect(patchOf({ Description: '', DocumentationLink: '  ', TimeZone: '' })).toEqual({
      Description: null,
      DocumentationLink: null,
      TimeZone: null,
    })
    expect(patchOf({ IsDisabled: true, ValidFrom: null, ExpiresAt: '2027-01-01T00:00:00Z' })).toEqual({
      IsDisabled: true,
      ValidFrom: null,
      ExpiresAt: '2027-01-01T00:00:00Z',
    })
    expect(toAnnotationPatch(base, edit({ IsDisabled: true }))).toEqual({ IsDisabled: null })
  })

  it('sends the labels whole when they changed, null when emptied', () => {
    expect(patchOf({ Labels: ['eu', 'pci', 'gdpr'] })).toEqual({ Labels: ['eu', 'pci', 'gdpr'] })
    expect(patchOf({ Labels: ['pci'] })).toEqual({ Labels: ['pci'] })
    expect(patchOf({ Labels: [] })).toEqual({ Labels: null })
  })

  it('diffs Values name by name and recursively: a removed name or nested member is null', () => {
    expect(
      patchOf({
        Values: { plan: 'pro', limits: { cpu: 2, memory: 8, disk: 10 }, regions: ['eu', 'us'], tier: 'gold' },
      }),
    ).toEqual({
      Values: { plan: 'pro', retries: null, limits: { memory: 8, disk: 10 }, regions: ['eu', 'us'], tier: 'gold' },
    })
    expect(patchOf({ Values: { ...loaded.Values, limits: { cpu: 2 } } })).toEqual({
      Values: { limits: { memory: null } },
    })
    expect(patchOf({ Values: {} })).toEqual({
      Values: { plan: null, retries: null, limits: null, regions: null },
    })
  })

  it('replaces an array that shrinks, and an object that becomes an array and back, whole', () => {
    expect(patchOf({ Values: { ...loaded.Values, regions: [] } })).toEqual({ Values: { regions: [] } })
    expect(patchOf({ Values: { ...loaded.Values, limits: [2, 4] } })).toEqual({ Values: { limits: [2, 4] } })
    const asArray = { ...loaded, Values: { ...loaded.Values, limits: [2, 4] } } as Annotation
    expect(toAnnotationPatch(edit({ Values: loaded.Values }), toAnnotationDraft(asArray))).toEqual({
      Values: { limits: { cpu: 2, memory: 4 } },
    })
  })

  it('takes a value named __proto__ as a name', () => {
    const values = JSON.parse('{"plan": "basic", "__proto__": {"x": 1}}') as Record<string, unknown>
    const patch = patchOf({ Values: { ...loaded.Values, ...values } }) as { Values: Record<string, unknown> }

    expect(Object.keys(patch.Values)).toEqual(['__proto__'])
    expect(JSON.stringify(patch)).toBe('{"Values":{"__proto__":{"x":1}}}')
  })

  it('replaces a value whose type changes, an array whole', () => {
    expect(patchOf({ Values: { ...loaded.Values, limits: 'none', regions: [{ name: 'eu', zone: null }] } })).toEqual({
      Values: { limits: 'none', regions: [{ name: 'eu', zone: null }] },
    })
  })

  it('sends the trigger of a unit only, trimmed, and never on other types', () => {
    const unit = {
      ...loaded,
      AnnotationKey: 'unt.invoicing.pdf',
      AnnotationType: 'Unit',
      UnitType: 'event',
      UnitDefinition: 'x',
    } as Annotation
    const unitBase = toAnnotationDraft(unit)

    expect(toAnnotationPatch({ ...unitBase, UnitDefinition: ' orders.> ' }, unitBase)).toEqual({
      UnitDefinition: 'orders.>',
    })
    expect(toAnnotationPatch({ ...unitBase, Title: 'PDF' }, unitBase)).toEqual({ Title: 'PDF' })
    expect(patchOf({ Title: 'Other', UnitType: 'queue' } as Partial<AnnotationForm>)).toEqual({ Title: 'Other' })
  })

  it('leaves out members the text modes add outside the editable document', () => {
    expect(patchOf({ Title: 'X', SubjectKey: 'sbt.other', Foo: 1 } as Partial<AnnotationForm>)).toEqual({ Title: 'X' })
  })
})

describe('toAnnotationPatch: a JSON Patch when a value is set to null', () => {
  it('replaces each changed member and adds the changed value names, escaped', () => {
    expect(patchOf({ Title: 'X', Labels: ['eu'], Values: { ...loaded.Values, plan: null, 'a/b~c': 1 } })).toEqual([
      { op: 'replace', path: '/Title', value: 'X' },
      { op: 'replace', path: '/Labels', value: ['eu'] },
      { op: 'add', path: '/Values/plan', value: null },
      { op: 'add', path: '/Values/a~1b~0c', value: 1 },
    ])
  })

  it('removes a name after adding it, so a name removed meanwhile does not fail', () => {
    const { plan: _plan, ...rest } = loaded.Values as Record<string, unknown>
    expect(patchOf({ Values: { ...rest, retries: null } })).toEqual([
      { op: 'add', path: '/Values/retries', value: null },
      { op: 'add', path: '/Values/plan', value: null },
      { op: 'remove', path: '/Values/plan' },
    ])
  })

  it('also for a null member of an object value, which a merge would drop', () => {
    expect(patchOf({ Values: { ...loaded.Values, limits: { cpu: 2, memory: null } } })).toEqual([
      { op: 'add', path: '/Values/limits', value: { cpu: 2, memory: null } },
    ])
  })
})

describe('toAnnotationPatch against the mock API', () => {
  // The stored annotation moved on after the form loaded: another title, another value, another label.
  const stored = {
    ...loaded,
    Title: 'Renamed by someone else',
    Labels: ['eu', 'pci', 'gdpr'],
    Values: { ...loaded.Values, owner: 'ops', limits: { cpu: 4, memory: 4 } },
  } as Annotation

  const applied = (changes: Partial<AnnotationForm>) => {
    const patch = patchOf(changes)!
    const result = applyAnnotationPatch(stored, Array.isArray(patch) ? 'json-patch' : 'merge', patch)
    if (!('annotation' in result)) {
      throw new Error(result.message)
    }
    return result.annotation
  }

  it('keeps the changes of others to members and values the user did not touch', () => {
    expect(
      applied({ Description: 'Edited', Values: { ...loaded.Values, limits: { cpu: 2, memory: 8 } } }),
    ).toMatchObject({
      Title: 'Renamed by someone else',
      Description: 'Edited',
      Values: { plan: 'basic', owner: 'ops', limits: { cpu: 4, memory: 8 } },
    })
  })

  it('writes a null value with the JSON Patch and keeps the other values', () => {
    expect(applied({ Values: { ...loaded.Values, plan: null } }).Values).toEqual({
      ...stored.Values,
      plan: null,
    })
  })
})

describe('editableOf and sameValue', () => {
  it('normalizes a draft as the API stores it', () => {
    expect(editableOf(edit({ Title: '  ', IsDisabled: false, Labels: [' eu '] }))).toMatchObject({
      Title: null,
      IsDisabled: null,
      Labels: ['eu'],
    })
  })

  it('compares JSON values deeply, ignoring the order of names', () => {
    expect(sameValue({ a: [1, { b: 2 }], c: null }, { c: null, a: [1, { b: 2 }] })).toBe(true)
    expect(sameValue({ a: [1, 2] }, { a: [2, 1] })).toBe(false)
    expect(sameValue({ a: 1 }, { a: 1, b: undefined })).toBe(false)
    expect(sameValue(null, {})).toBe(false)
  })
})

describe('labelAddition and labelRemoval', () => {
  it('append a label where no spelling of it is, guarded by the labels read', () => {
    expect(labelAddition(['eu'], ' pci ')).toEqual([
      { op: 'test', path: '/Labels', value: ['eu'] },
      { op: 'add', path: '/Labels/-', value: 'pci' },
    ])
    expect(labelAddition(['eu', 'PCI'], 'pci')).toBeUndefined()
  })

  it('remove every spelling of the label, highest index first, guarded by the labels read', () => {
    expect(labelRemoval(['eu', 'pci', ' EU '], 'Eu')).toEqual([
      { op: 'test', path: '/Labels', value: ['eu', 'pci', ' EU '] },
      { op: 'remove', path: '/Labels/2' },
      { op: 'remove', path: '/Labels/0' },
    ])
    expect(labelRemoval(['pci'], 'eu')).toBeUndefined()
    expect(sameLabel(' GDPR', 'gdpr ')).toBe(true)
  })

  it('see the labels as a patch does: strings only', () => {
    expect(patchableLabels(['eu', null, 'pci'])).toEqual(['eu', 'pci'])
    expect(patchableLabels(undefined)).toEqual([])
  })
})
