import { describe, expect, it } from 'vitest'
import {
  annotationTypes,
  builtInTypeAppearances,
  isCatalogTimeZone,
  isDateFormat,
  isTypeColor,
  isTypeIconName,
  projectSettingsEntityTag,
  projectSettingsTypeCodes,
  typeIconNames,
  validateProjectSettings,
  StorytellerError,
  type ErrorResponse,
  type NormalizedProjectSettings,
  type ProjectSettings,
} from '../index'

describe('project settings helpers', () => {
  it('builds the ETag of a version as the API sends it', () => {
    expect(projectSettingsEntityTag(0)).toBe('"0"')
    expect(projectSettingsEntityTag(7)).toBe('"7"')
  })

  it.each([-1, 1.5, Number.NaN, Number.MAX_SAFE_INTEGER + 1])('refuses %s as a version', version => {
    expect(() => projectSettingsEntityTag(version)).toThrow(RangeError)
  })

  it('knows the values of the catalog in its spelling only', () => {
    expect(isTypeColor('pink')).toBe(true)
    expect(isTypeColor('PINK')).toBe(false)
    expect(isTypeColor('beige')).toBe(false)
    expect(isTypeIconName('i-lucide-building-2')).toBe(true)
    expect(isTypeIconName('building-2')).toBe(false)
    expect(isDateFormat('d mmm yyyy')).toBe(true)
    expect(isDateFormat('iso')).toBe(false)
    expect(isCatalogTimeZone('Europe/Prague')).toBe(true)
    expect(isCatalogTimeZone('Asia/Calcutta')).toBe(true)
    expect(isCatalogTimeZone('europe/prague')).toBe(false)
    expect(isCatalogTimeZone('Central Europe Standard Time')).toBe(false)
    expect([undefined, null, 1, {}].some(value => isTypeColor(value) || isTypeIconName(value) || isDateFormat(value) || isCatalogTimeZone(value))).toBe(false)
  })

  it('has the type codes of the annotation types, and their built-in appearance', () => {
    expect([...projectSettingsTypeCodes]).toEqual(annotationTypes.map(type => type.code))

    for (const code of projectSettingsTypeCodes) {
      const builtIn = builtInTypeAppearances[code]

      expect(isTypeColor(builtIn.color), code).toBe(true)
      expect(isTypeIconName(builtIn.icon), code).toBe(true)
    }

    expect(typeIconNames).toContain('i-lucide-building-2')
  })

  it('reads a GET answer as it is, and the settings it returns go back in a PUT', () => {
    const read: ProjectSettings = {
      Types: { sbt: { Label: ' Company ', Color: 'Pink' } },
      DefaultTimeZone: 'europe/prague',
      Version: 7,
      Modified: { At: '2026-11-15T10:00:00+00:00', By: { Kind: 'Account', Id: 'user-1', Name: 'Ada' } },
    }
    const before = structuredClone(read)

    const validation = validateProjectSettings(read)

    expect(validation.valid).toBe(true)
    expect(validation.settings).toEqual({ Types: { sbt: { Label: 'Company', Color: 'pink' } }, DefaultTimeZone: 'Europe/Prague' })
    expect(read).toEqual(before)

    // The normalized settings are a body of setProjectSettings.
    const body: ProjectSettings = validation.settings as NormalizedProjectSettings
    expect(body.Types?.sbt?.Color).toBe('pink')
  })

  it('reports the problems in the order the API reports them', () => {
    const validation = validateProjectSettings({
      Theme: 'dark',
      Types: { sbt: { Color: 'beige' }, xyz: {} },
      DateFormat: 'iso',
    })

    expect(validation.valid).toBe(false)
    expect(validation.problems.map(problem => `${problem.path} ${problem.rule}`)).toEqual([
      '$.Theme unknown-member',
      '$.Types.xyz type-code',
      '$.Types.sbt.Color color',
      '$.DateFormat date-format',
    ])
    expect(validation.problems[2]?.message).toBe("'beige' is not a color of the catalog; use one of red, orange, amber, yellow, lime, green, emerald, teal, cyan, sky, blue, indigo, violet, purple, fuchsia, pink, rose, gray.")
  })

  it('trims Unicode white space as the API does, not as String.prototype.trim', () => {
    // U+0085 is White_Space (trimmed by .NET, kept by trim()); U+FEFF is not (kept by .NET, removed by trim()).
    expect(validateProjectSettings({ Types: { sbt: { Label: '\u0085Company\u0085' } } }).settings?.Types.sbt?.Label).toBe('Company')
    expect(validateProjectSettings({ Types: { sbt: { Label: '﻿Company' } } }).settings?.Types.sbt?.Label).toBe('﻿Company')
    expect(validateProjectSettings({ DefaultTimeZone: '\u0085' }).settings).toEqual({ Types: {} })
  })

  it('refuses a control character in a label or a plural, and names the field', () => {
    const validation = validateProjectSettings({ Types: { sbt: { Label: 'Com\u001Bpany', Plural: 'Compa\u2028nies' }, cnt: { Label: '\u202EtnemtrapeD' } } })

    expect(validation.problems).toEqual([
      { path: '$.Types.sbt.Label', rule: 'control-character', message: 'The label contains the character U+001B; control characters, line and paragraph separators and bidirectional controls are not allowed.' },
      { path: '$.Types.sbt.Plural', rule: 'control-character', message: 'The plural contains the character U+2028; control characters, line and paragraph separators and bidirectional controls are not allowed.' },
      { path: '$.Types.cnt.Label', rule: 'control-character', message: 'The label contains the character U+202E; control characters, line and paragraph separators and bidirectional controls are not allowed.' },
    ])
    // Trimmed at either end before the check; a zero width joiner is not a control.
    expect(validateProjectSettings({ Types: { sbt: { Label: '\u0009Company\u2029' } } }).settings?.Types.sbt?.Label).toBe('Company')
    expect(validateProjectSettings({ Types: { sbt: { Label: 'Zero\u200Dwidth' } } }).valid).toBe(true)
  })

  it('keeps the problems of a 400 InvalidSettings, whose Details are an array', () => {
    const body: ErrorResponse = {
      Message: 'The project settings have 1 problem; the first is $.DateFormat: no.',
      ErrorCode: 'InvalidSettings',
      Details: [{ Path: '$.DateFormat', Rule: 'date-format', Message: 'no.' }],
    }

    const error = StorytellerError.fromResponse(400, 'Bad Request', body)

    expect(error.errorCode).toBe('InvalidSettings')
    expect((error.body as ErrorResponse).Details).toEqual([{ Path: '$.DateFormat', Rule: 'date-format', Message: 'no.' }])
  })

  it('reads an object without a prototype, and refuses one that is not JSON data', () => {
    const document = Object.assign(Object.create(null) as object, { DateFormat: 'dd.mm.yyyy' })

    expect(validateProjectSettings(document).settings).toEqual({ Types: {}, DateFormat: 'dd.mm.yyyy' })
    expect(validateProjectSettings(new Map()).problems.map(problem => problem.rule)).toEqual(['type'])
    expect(validateProjectSettings(undefined).problems.map(problem => problem.path)).toEqual(['$'])
  })
})
