import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { validateProjectSettings, type ProjectSettings } from '@42for.net/2splatform.sdk'
import { builtInTypeMeta, typeCodes } from '@/utils/annotation-key'
import { dateFormats, defaultDateFormat } from '@/utils/dates'
import { defaultTimeZone, fromApi, toApi } from '@/utils/project-settings'
import {
  builtInTypeColors,
  maxTypeNameLength,
  sanitizeTypeAppearances,
  typeColors,
  typeIcons,
} from '@/utils/type-appearance'

// The UI keeps its own literal colors, icons and date formats (the icons must stay literal for Vite's icon scan and
// the CSP), so they are checked against the catalog of project settings of the conformance suite, which the API and
// both SDKs follow (424-266, spec section 5): whatever the UI offers and keeps, the API accepts.

interface CatalogAppearance {
  label: string
  plural: string
  color: string
  icon: string
}

interface Catalog {
  typeCodes: string[]
  builtIn: Record<string, CatalogAppearance>
  colors: string[]
  icons: string[]
  dateFormats: string[]
  defaults: { timeZone: string; dateFormat: string }
  limits: { label: number; plural: number }
  timeZones: string[]
}

interface Vector {
  name: string
  settings: unknown
  expected?: ProjectSettings
}

const suite = JSON.parse(
  // relative to the folder of the tests, as main.css in the palette test
  readFileSync('../../Storyteller/conformance/project-settings.json', 'utf8'),
) as { catalog: Catalog; vectors: Vector[] }
const { catalog, vectors } = suite

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

describe('the catalog of project settings', () => {
  it('has the type codes of the UI, in its order', () => {
    expect(catalog.typeCodes).toEqual(typeCodes)
  })

  it('has the colors of the palette, in its order', () => {
    expect(catalog.colors).toEqual(Object.keys(typeColors))
  })

  it('has the icons the UI offers, in its order', () => {
    expect(catalog.icons).toEqual(typeIcons.map((icon) => icon.name))
  })

  it('has the date formats of the UI, in its order', () => {
    expect(catalog.dateFormats).toEqual(dateFormats.map((format) => format.value))
  })

  it('limits the names as the UI does', () => {
    expect(catalog.limits).toEqual({ label: maxTypeNameLength, plural: maxTypeNameLength })
  })

  it('has the built-in names, colors and icons of the UI', () => {
    const builtIn = Object.fromEntries(
      typeCodes.map((code) => [
        code,
        {
          label: builtInTypeMeta[code].label,
          plural: builtInTypeMeta[code].plural,
          color: builtInTypeColors[code],
          icon: builtInTypeMeta[code].icon,
        },
      ]),
    )
    expect(catalog.builtIn).toEqual(builtIn)
  })

  it('has the defaults of the UI', () => {
    expect(catalog.defaults).toEqual({ timeZone: defaultTimeZone, dateFormat: defaultDateFormat })
    expect(catalog.timeZones).toContain(defaultTimeZone)
  })

  it('lists every time zone the time zone select offers here', () => {
    const offered = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []
    const listed = new Set(catalog.timeZones)
    expect(offered.filter((zone) => !listed.has(zone))).toEqual([])
  })
})

describe('the vectors of the catalog through the UI', () => {
  it('has vectors to check', () => {
    expect(vectors.length).toBeGreaterThan(50)
  })

  // The UI reads an answer with fromApi and writes a draft with toApi: the API never refuses what it keeps.
  it.each(vectors.map((vector) => [vector.name, vector] as const))(
    'the API accepts what the UI keeps of: %s',
    (_, vector) => {
      const kept = fromApi(vector.settings as ProjectSettings)
      const body = toApi(kept, kept)
      const check = validateProjectSettings(body)

      expect(check.problems).toEqual([])
      expect(check.valid).toBe(true)
    },
  )

  // The types of every vector in any case of their names, as the editor of the UI keeps them (Settings, Preferences).
  it.each(vectors.map((vector) => [vector.name, vector] as const))(
    'the API accepts what the sanitizer of the UI keeps of the types of: %s',
    (_, vector) => {
      const types = isObject(vector.settings)
        ? Object.entries(vector.settings).find(([name]) => name.toLowerCase() === 'types')?.[1]
        : undefined
      const appearances = isObject(types)
        ? Object.fromEntries(
            Object.entries(types).map(([code, appearance]) => [
              code.toLowerCase(),
              isObject(appearance)
                ? Object.fromEntries(Object.entries(appearance).map(([name, value]) => [name.toLowerCase(), value]))
                : appearance,
            ]),
          )
        : types
      const body = toApi({
        types: sanitizeTypeAppearances(appearances),
        defaultTimeZone,
        dateFormat: defaultDateFormat,
      })

      expect(validateProjectSettings(body).problems).toEqual([])
    },
  )

  // What the API stores, the UI shows and writes back unchanged.
  it.each(vectors.filter((vector) => vector.expected).map((vector) => [vector.name, vector] as const))(
    'the UI keeps what the API stores of: %s',
    (_, vector) => {
      const stored = vector.expected!
      const kept = fromApi(stored)

      expect(toApi(kept, kept)).toEqual(stored)
    },
  )
})
