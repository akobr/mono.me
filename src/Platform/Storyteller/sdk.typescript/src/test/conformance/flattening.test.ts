import { describe, expect, it } from 'vitest'
import { flattenConfiguration, InvalidDocumentError, type ConfigurationTarget } from '../../index'
import { filesOfKind, readConformance, type FlatteningFile, type FlatteningVector } from './conformance'

const targets: readonly string[] = ['flat', 'properties', 'env'] satisfies ConfigurationTarget[]
const files = filesOfKind('flattening').map(path => ({ path, file: readConformance<FlatteningFile>(path) }))

function run(vector: FlatteningVector, target: ConfigurationTarget) {
  if (vector.error) {
    const error = catchError(() => flattenConfiguration(vector.document, target))

    expect(error).toBeInstanceOf(InvalidDocumentError)
    expect({ rule: (error as InvalidDocumentError).rule, path: (error as InvalidDocumentError).path }).toEqual(vector.error)
    expect((error as InvalidDocumentError).message).toContain(vector.error.path)
    return
  }

  // The exact text of every key and value, in document order.
  expect(Object.entries(flattenConfiguration(vector.document, target))).toEqual(Object.entries(vector.keys ?? {}))
}

function catchError(action: () => unknown): unknown {
  try {
    action()
  } catch (error) {
    return error
  }

  throw new Error('The vector expects an error, but there was none.')
}

describe('flattening vectors', () => {
  it('has a file for every target of this runner, and no other', () => {
    const fileTargets = files.map(({ file }) => file.target)

    expect(fileTargets).toHaveLength(targets.length)
    expect(new Set(fileTargets)).toEqual(new Set(targets))
  })

  describe.each(files)('$path', ({ file }) => {
    it.each(file.vectors)('$name', vector => {
      expect(targets).toContain(file.target)
      run(vector, file.target as ConfigurationTarget)
    })
  })
})
