import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { builtInTypeMeta, typeCodes } from '@/utils/annotation-key'
import {
  builtInTypeColors,
  mergeTypeAppearances,
  pluralize,
  resolveTypeAppearances,
  sanitizeTypeAppearances,
  typeColors,
  typeColorsCss,
  typeIcons,
} from '@/utils/type-appearance'

describe('sanitizeTypeAppearances', () => {
  it('keeps only known codes, palette colors and bundled icons', () => {
    expect(
      sanitizeTypeAppearances({
        exe: { label: 'Job', color: 'red', icon: 'i-lucide-rocket' },
        sbt: { color: 'chartreuse', icon: 'i-lucide-not-bundled' },
        xyz: { label: 'Unknown' },
        cnt: 'not an object',
      }),
    ).toEqual({ exe: { label: 'Job', color: 'red', icon: 'i-lucide-rocket' } })
    expect(sanitizeTypeAppearances(undefined)).toEqual({})
    expect(sanitizeTypeAppearances(['exe'])).toEqual({})
  })

  it('trims names, cuts them to 40 characters and drops empty ones', () => {
    expect(sanitizeTypeAppearances({ exe: { label: '  Batch job  ', plural: ' Batch jobs ' } })).toEqual({
      exe: { label: 'Batch job', plural: 'Batch jobs' },
    })
    expect(sanitizeTypeAppearances({ exe: { label: 'x'.repeat(50) } }).exe?.label).toHaveLength(40)
    expect(sanitizeTypeAppearances({ exe: { label: '   ' } })).toEqual({})
  })

  it('drops a plural without a name, because names come as a pair', () => {
    expect(sanitizeTypeAppearances({ exe: { plural: 'Jobs', color: 'red' } })).toEqual({ exe: { color: 'red' } })
  })
})

describe('pluralize', () => {
  it('follows the regular English endings', () => {
    expect(pluralize('Job')).toBe('Jobs')
    expect(pluralize('Policy')).toBe('Policies')
    expect(pluralize('Day')).toBe('Days')
    expect(pluralize('Batch')).toBe('Batches')
    expect(pluralize('Box')).toBe('Boxes')
    expect(pluralize('Class')).toBe('Classes')
  })
})

describe('mergeTypeAppearances', () => {
  it('takes each field from the highest layer that sets it', () => {
    const personal = { exe: { icon: 'i-lucide-zap' } }
    const project = { exe: { label: 'Job', plural: 'Jobs', color: 'red' as const, icon: 'i-lucide-rocket' } }

    expect(mergeTypeAppearances(personal, project)).toEqual({
      exe: { label: 'Job', plural: 'Jobs', color: 'red', icon: 'i-lucide-zap' },
    })
  })

  it('takes the name and its plural from the same layer', () => {
    const personal = { exe: { label: 'Run' } }
    const project = { exe: { label: 'Job', plural: 'Jobs' } }

    expect(mergeTypeAppearances(personal, project).exe).toEqual({ label: 'Run', plural: 'Runs' })
    expect(mergeTypeAppearances({}, project).exe).toEqual({ label: 'Job', plural: 'Jobs' })
  })

  it('resolves to the built-in values below the layers', () => {
    const resolved = resolveTypeAppearances({ exe: { color: 'red' } })

    expect(resolved.exe).toEqual({ label: 'Execution', plural: 'Executions', color: 'red', icon: 'i-lucide-play' })
    expect(resolved.sbt).toEqual({ label: 'Subject', plural: 'Subjects', color: 'blue', icon: 'i-lucide-sun' })
  })
})

describe('typeColorsCss', () => {
  it('overrides the tokens of the changed types only, in both themes', () => {
    const css = typeColorsCss({ exe: { color: 'red' }, sbt: { label: 'Customer' } })

    expect(css).toContain(':root:not(.dark) {\n  --st-exe: #b91c1c;\n}')
    expect(css).toContain(':root.dark {\n  --st-exe: #f87171;\n}')
    expect(css).not.toContain('--st-sbt')
    expect(typeColorsCss({})).toBe('')
  })
})

describe('palette', () => {
  const rgb = (hex: string) => [1, 3, 5].map((index) => parseInt(hex.slice(index, index + 2), 16))
  const luminance = (color: number[]) => {
    const [r, g, b] = color.map((channel) => {
      const value = channel / 255
      return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
    })
    return 0.2126 * r! + 0.7152 * g! + 0.0722 * b!
  }
  const contrast = (a: number[], b: number[]) => {
    const [high, low] = [luminance(a), luminance(b)].toSorted((x, y) => y - x)
    return (high! + 0.05) / (low! + 0.05)
  }
  // A chip's background: the color at 10 % over the panel (bg-st-*/10).
  const tint = (color: number[], panel: number[]) => color.map((channel, index) => channel * 0.1 + panel[index]! * 0.9)
  const light = rgb('#ffffff')
  const dark = rgb('#18181b') // zinc-900, the dark panel

  it.each(Object.entries(typeColors))('%s keeps 4.5:1 on the panel and on a chip in both themes', (_, color) => {
    for (const [value, panel] of [
      [rgb(color.light), light],
      [rgb(color.dark), dark],
    ] as const) {
      expect(contrast(value, panel)).toBeGreaterThanOrEqual(4.5)
      expect(contrast(value, tint(value, panel))).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('holds the built-in colors with the values of main.css', () => {
    const css = readFileSync('app/assets/css/main.css', 'utf8')
    const [lightBlock, darkBlock] = [css.indexOf(':root {'), css.indexOf('.dark {')].map((start) =>
      css.slice(start, css.indexOf('}', start)),
    )

    for (const code of typeCodes) {
      const color = typeColors[builtInTypeColors[code]]
      expect(lightBlock).toContain(`--st-${code}: ${color.light};`)
      expect(darkBlock).toContain(`--st-${code}: ${color.dark};`)
    }
  })

  it('offers the built-in icons', () => {
    const names = typeIcons.map((icon) => icon.name)
    for (const code of typeCodes) {
      expect(names).toContain(builtInTypeMeta[code].icon)
    }
    expect(new Set(names).size).toBe(names.length)
  })
})
