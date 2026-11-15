import { describe, expect, it } from 'vitest'
import {
  builtInTypeAppearances,
  catalogTimeZones,
  dateFormats,
  projectSettingsDefaults,
  projectSettingsFormat,
  projectSettingsLimits,
  projectSettingsRules,
  projectSettingsTypeCodes,
  typeColors,
  typeIconNames,
  validateProjectSettings,
} from '../../index'
import { filesOfKind, readConformance, type ProjectSettingsFile } from './conformance'

const files = filesOfKind('project-settings').map(path => ({ path, file: readConformance<ProjectSettingsFile>(path) }))

describe('project settings vectors', () => {
  it('has a file', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  describe.each(files)('$path', ({ file }) => {
    // generated/catalog/project-settings.ts is written from this file by npm run generate.
    it('is the catalog the SDK exports', () => {
      const { catalog } = file

      expect(projectSettingsFormat).toBe(catalog.format)
      expect([...projectSettingsTypeCodes]).toEqual(catalog.typeCodes)
      expect(builtInTypeAppearances).toEqual(catalog.builtIn)
      expect([...typeColors]).toEqual(catalog.colors)
      expect([...typeIconNames]).toEqual(catalog.icons)
      expect([...dateFormats]).toEqual(catalog.dateFormats)
      expect(projectSettingsDefaults).toEqual(catalog.defaults)
      expect(projectSettingsLimits).toEqual(catalog.limits)
      expect([...catalogTimeZones]).toEqual(catalog.timeZones)
    })

    it('refuses by every rule', () => {
      const rules = new Set(file.vectors.flatMap(vector => vector.problems ?? []).map(problem => problem.rule))

      expect(rules).toEqual(new Set(projectSettingsRules))
    })

    // The normalized settings (the order of the members does not count), or every problem by its path and rule, in any
    // order, as the .NET reference compares them.
    it.each(file.vectors)('$name', vector => {
      const validation = validateProjectSettings(vector.settings)

      if (vector.expected) {
        expect(validation.problems).toEqual([])
        expect(validation.valid).toBe(true)
        expect(validation.settings).toEqual(vector.expected)
      } else {
        expect(validation.valid).toBe(false)
        expect(validation.settings).toBeUndefined()
        const problems = validation.problems.map(problem => `${problem.path} ${problem.rule}`)
        const expected = (vector.problems ?? []).map(problem => `${problem.path} ${problem.rule}`)
        expect(problems).toHaveLength(expected.length)
        expect(problems).toEqual(expect.arrayContaining(expected))
        expect(validation.problems.every(problem => problem.message.trim().length > 0)).toBe(true)
      }
    })

    // What the SDK accepts it accepts again as it wrote it, with the same values.
    it.each(file.vectors.filter(vector => vector.expected))('$name reads the same again', vector => {
      const first = validateProjectSettings(vector.settings)
      const again = validateProjectSettings(first.settings)

      expect(again.problems).toEqual([])
      expect(again.settings).toEqual(first.settings)
    })
  })
})
