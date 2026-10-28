import { describe, expect, it } from 'vitest'
import { formatConfiguration, InvalidDocumentError } from '../../index'
import { filesOfKind, readConformance, type FormatsFile } from './conformance'

// formatConfiguration writes flat and env; the .NET runner pins the text of properties and yaml.
const implemented: readonly string[] = ['flat', 'env']
const files = filesOfKind('formats').map(path => ({ path, file: readConformance<FormatsFile>(path) }))

describe('format vectors', () => {
  describe.each(files)('$path', ({ file }) => {
    const run = file.vectors.filter(vector => implemented.includes(vector.format))
    const leftToDotNet = file.vectors.filter(vector => !implemented.includes(vector.format))

    it(`leaves ${leftToDotNet.length} vectors of properties and yaml to the .NET runner`, () => {
      expect(new Set(leftToDotNet.map(vector => vector.format))).toEqual(new Set(['properties', 'yaml']))
    })

    it.each(run)('$format: $name', vector => {
      const format = vector.format as 'flat' | 'env'

      if (vector.error) {
        let error: unknown

        try {
          formatConfiguration(vector.document, format)
        } catch (thrown) {
          error = thrown
        }

        expect(error).toBeInstanceOf(InvalidDocumentError)
        expect({ rule: (error as InvalidDocumentError).rule, path: (error as InvalidDocumentError).path }).toEqual(vector.error)
        return
      }

      expect(formatConfiguration(vector.document, format)).toBe(vector.text)
    })
  })
})
