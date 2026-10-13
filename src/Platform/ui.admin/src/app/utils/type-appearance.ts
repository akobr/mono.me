import { builtInTypeMeta, typeCodes, typeMeta, type AnnotationTypeCode } from './annotation-key'

/**
 * Names, colors and icons of the annotation types, overridable per project (Settings) and per person (Preferences).
 * Layers are resolved per field, highest first: personal, then project, then built-in. The codes and the API type
 * names never change; they are data, not presentation.
 */

export type TypeColor =
  | 'red'
  | 'orange'
  | 'amber'
  | 'yellow'
  | 'lime'
  | 'green'
  | 'emerald'
  | 'teal'
  | 'cyan'
  | 'sky'
  | 'blue'
  | 'indigo'
  | 'violet'
  | 'purple'
  | 'fuchsia'
  | 'pink'
  | 'rose'
  | 'gray'

/** One layer's overrides for one type; a missing field falls through to the next layer. */
export interface TypeAppearance {
  label?: string
  plural?: string
  color?: TypeColor
  icon?: string
}

export type TypeAppearances = Partial<Record<AnnotationTypeCode, TypeAppearance>>

export interface ResolvedTypeAppearance {
  label: string
  plural: string
  color: TypeColor
  icon: string
}

/**
 * The palette users pick from. Light values are Tailwind 700 shades (800 for amber, orange, yellow, lime and green)
 * and dark values 400 shades, so text in the color keeps 4.5:1 on the panel and on a chip's 10 % tint in both themes.
 * The seven built-in colors are the values of the --st-* tokens in main.css.
 */
export const typeColors: Record<TypeColor, { label: string; light: string; dark: string }> = {
  red: { label: 'Red', light: '#b91c1c', dark: '#f87171' },
  orange: { label: 'Orange', light: '#9a3412', dark: '#fb923c' },
  amber: { label: 'Amber', light: '#92400e', dark: '#fbbf24' },
  yellow: { label: 'Yellow', light: '#854d0e', dark: '#facc15' },
  lime: { label: 'Lime', light: '#3f6212', dark: '#a3e635' },
  green: { label: 'Green', light: '#166534', dark: '#4ade80' },
  emerald: { label: 'Emerald', light: '#047857', dark: '#34d399' },
  teal: { label: 'Teal', light: '#0f766e', dark: '#2dd4bf' },
  cyan: { label: 'Cyan', light: '#0e7490', dark: '#22d3ee' },
  sky: { label: 'Sky', light: '#0369a1', dark: '#38bdf8' },
  blue: { label: 'Blue', light: '#1d4ed8', dark: '#60a5fa' },
  indigo: { label: 'Indigo', light: '#4338ca', dark: '#818cf8' },
  violet: { label: 'Violet', light: '#6d28d9', dark: '#a78bfa' },
  purple: { label: 'Purple', light: '#7e22ce', dark: '#c084fc' },
  fuchsia: { label: 'Fuchsia', light: '#a21caf', dark: '#e879f9' },
  pink: { label: 'Pink', light: '#be185d', dark: '#f472b6' },
  rose: { label: 'Rose', light: '#be123c', dark: '#fb7185' },
  gray: { label: 'Gray', light: '#52525b', dark: '#a1a1aa' },
}

export const builtInTypeColors: Record<AnnotationTypeCode, TypeColor> = {
  rst: 'violet',
  unt: 'fuchsia',
  sbt: 'blue',
  usg: 'amber',
  cnt: 'cyan',
  exe: 'emerald',
  uxe: 'orange',
}

/**
 * The icons users pick from. Only icons named in the source are bundled (vite.config.ts, icon scan) and the CSP
 * blocks the Iconify API, so any other name would render nothing. Keep the names literal.
 */
export const typeIcons: { name: string; label: string }[] = [
  { name: 'i-lucide-target', label: 'Target' },
  { name: 'i-lucide-box', label: 'Box' },
  { name: 'i-lucide-sun', label: 'Sun' },
  { name: 'i-lucide-link', label: 'Link' },
  { name: 'i-lucide-layers', label: 'Layers' },
  { name: 'i-lucide-play', label: 'Play' },
  { name: 'i-lucide-cpu', label: 'CPU' },
  { name: 'i-lucide-activity', label: 'Activity' },
  { name: 'i-lucide-anchor', label: 'Anchor' },
  { name: 'i-lucide-atom', label: 'Atom' },
  { name: 'i-lucide-bell', label: 'Bell' },
  { name: 'i-lucide-bookmark', label: 'Bookmark' },
  { name: 'i-lucide-bot', label: 'Bot' },
  { name: 'i-lucide-boxes', label: 'Boxes' },
  { name: 'i-lucide-brain', label: 'Brain' },
  { name: 'i-lucide-briefcase', label: 'Briefcase' },
  { name: 'i-lucide-bug', label: 'Bug' },
  { name: 'i-lucide-building-2', label: 'Building' },
  { name: 'i-lucide-calendar', label: 'Calendar' },
  { name: 'i-lucide-circle', label: 'Circle' },
  { name: 'i-lucide-clock', label: 'Clock' },
  { name: 'i-lucide-cloud', label: 'Cloud' },
  { name: 'i-lucide-code', label: 'Code' },
  { name: 'i-lucide-cog', label: 'Cog' },
  { name: 'i-lucide-compass', label: 'Compass' },
  { name: 'i-lucide-component', label: 'Component' },
  { name: 'i-lucide-container', label: 'Container' },
  { name: 'i-lucide-crown', label: 'Crown' },
  { name: 'i-lucide-database', label: 'Database' },
  { name: 'i-lucide-factory', label: 'Factory' },
  { name: 'i-lucide-file', label: 'File' },
  { name: 'i-lucide-flag', label: 'Flag' },
  { name: 'i-lucide-flame', label: 'Flame' },
  { name: 'i-lucide-folder', label: 'Folder' },
  { name: 'i-lucide-gauge', label: 'Gauge' },
  { name: 'i-lucide-gem', label: 'Gem' },
  { name: 'i-lucide-git-branch', label: 'Branch' },
  { name: 'i-lucide-globe', label: 'Globe' },
  { name: 'i-lucide-heart', label: 'Heart' },
  { name: 'i-lucide-hexagon', label: 'Hexagon' },
  { name: 'i-lucide-key', label: 'Key' },
  { name: 'i-lucide-landmark', label: 'Landmark' },
  { name: 'i-lucide-layout-grid', label: 'Grid' },
  { name: 'i-lucide-leaf', label: 'Leaf' },
  { name: 'i-lucide-lightbulb', label: 'Light bulb' },
  { name: 'i-lucide-map-pin', label: 'Map pin' },
  { name: 'i-lucide-monitor', label: 'Monitor' },
  { name: 'i-lucide-network', label: 'Network' },
  { name: 'i-lucide-package', label: 'Package' },
  { name: 'i-lucide-puzzle', label: 'Puzzle' },
  { name: 'i-lucide-rocket', label: 'Rocket' },
  { name: 'i-lucide-server', label: 'Server' },
  { name: 'i-lucide-shield', label: 'Shield' },
  { name: 'i-lucide-smartphone', label: 'Smartphone' },
  { name: 'i-lucide-square', label: 'Square' },
  { name: 'i-lucide-star', label: 'Star' },
  { name: 'i-lucide-store', label: 'Store' },
  { name: 'i-lucide-tag', label: 'Tag' },
  { name: 'i-lucide-triangle', label: 'Triangle' },
  { name: 'i-lucide-truck', label: 'Truck' },
  { name: 'i-lucide-user', label: 'User' },
  { name: 'i-lucide-users', label: 'Users' },
  { name: 'i-lucide-workflow', label: 'Workflow' },
  { name: 'i-lucide-wrench', label: 'Wrench' },
  { name: 'i-lucide-zap', label: 'Zap' },
]

export const maxTypeNameLength = 40

const iconNames = new Set(typeIcons.map((icon) => icon.name))

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

function cleanName(value: unknown): string | undefined {
  return typeof value === 'string' ? value.trim().slice(0, maxTypeNameLength).trim() || undefined : undefined
}

/**
 * Keeps only what can be shown: known codes, palette colors, bundled icons and trimmed names of at most
 * 40 characters. A plural without a name is dropped (names come as a pair), and so are types with nothing left.
 */
export function sanitizeTypeAppearances(value: unknown): TypeAppearances {
  if (!isRecord(value)) {
    return {}
  }

  const result: TypeAppearances = {}
  for (const code of typeCodes) {
    const entry = value[code]
    if (!isRecord(entry)) {
      continue
    }

    const label = cleanName(entry.label)
    const appearance: TypeAppearance = {
      label,
      plural: label ? cleanName(entry.plural) : undefined,
      color: typeof entry.color === 'string' && entry.color in typeColors ? (entry.color as TypeColor) : undefined,
      icon: typeof entry.icon === 'string' && iconNames.has(entry.icon) ? entry.icon : undefined,
    }
    const defined = Object.fromEntries(Object.entries(appearance).filter(([, field]) => field !== undefined))
    if (Object.keys(defined).length > 0) {
      result[code] = defined
    }
  }
  return result
}

/** English plural of a type name: "Job" → "Jobs", "Policy" → "Policies", "Batch" → "Batches". */
export function pluralize(name: string): string {
  if (/[^aeiou]y$/i.test(name)) {
    return `${name.slice(0, -1)}ies`
  }
  return /(s|x|z|ch|sh)$/i.test(name) ? `${name}es` : `${name}s`
}

/**
 * Merges layers, highest first, per field. The name is a pair: the highest layer with a label decides the plural
 * too, derived from the label when that layer has none, so a personal "Run" never sits next to a project's "Jobs".
 */
export function mergeTypeAppearances(...layers: TypeAppearances[]): TypeAppearances {
  const result: TypeAppearances = {}
  for (const code of typeCodes) {
    const entries = layers.map((layer) => layer[code]).filter((entry) => entry !== undefined)
    const named = entries.find((entry) => entry.label)
    const merged: TypeAppearance = {
      label: named?.label,
      plural: named?.label ? (named.plural ?? pluralize(named.label)) : undefined,
      color: entries.find((entry) => entry.color)?.color,
      icon: entries.find((entry) => entry.icon)?.icon,
    }
    const defined = Object.fromEntries(Object.entries(merged).filter(([, field]) => field !== undefined))
    if (Object.keys(defined).length > 0) {
      result[code] = defined
    }
  }
  return result
}

/** The appearance of every type after the layers (highest first) and the built-in defaults. */
export function resolveTypeAppearances(
  ...layers: TypeAppearances[]
): Record<AnnotationTypeCode, ResolvedTypeAppearance> {
  const merged = mergeTypeAppearances(...layers)
  return Object.fromEntries(
    typeCodes.map((code) => {
      const builtIn = builtInTypeMeta[code]
      const own = merged[code]
      return [
        code,
        {
          label: own?.label ?? builtIn.label,
          plural: own?.plural ?? builtIn.plural,
          color: own?.color ?? builtInTypeColors[code],
          icon: own?.icon ?? builtIn.icon,
        },
      ]
    }),
  ) as Record<AnnotationTypeCode, ResolvedTypeAppearance>
}

/**
 * CSS for the overridden colors only. The selectors outrank `:root` and `.dark` of main.css, so the order in which
 * the style sheets load does not matter.
 */
export function typeColorsCss(appearances: TypeAppearances): string {
  const colored = typeCodes.flatMap((code) => {
    const color = appearances[code]?.color
    return color ? [[code, typeColors[color]] as const] : []
  })
  if (colored.length === 0) {
    return ''
  }

  const block = (selector: string, theme: 'light' | 'dark') =>
    `${selector} {\n${colored.map(([code, color]) => `  --st-${code}: ${color[theme]};`).join('\n')}\n}`
  return `${block(':root:not(.dark)', 'light')}\n${block(':root.dark', 'dark')}\n`
}

/** Writes the merged names and icons into the shared, reactive `typeMeta`; types without overrides get the built-in ones. */
export function applyTypeAppearance(appearances: TypeAppearances): void {
  const resolved = resolveTypeAppearances(appearances)
  for (const code of typeCodes) {
    const meta = typeMeta[code]
    meta.label = resolved[code].label
    meta.plural = resolved[code].plural
    meta.icon = resolved[code].icon
  }
}
