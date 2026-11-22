import type { LocationQuery, LocationQueryValue, RouteLocationRaw } from 'vue-router'
import type { Scope } from '@/stores/context'
import { lineStats } from '@/utils/line-diff'
import type { ConfigurationContent, ViewItemKind } from '@/utils/view-documents'

// The address of the comparison of a selection across two views, and the result of comparing two documents (spec
// "Copy and Compare Annotations and Configurations Between Views", section 3). The source is the view of the route,
// the target the view named by ?to=.

/** The keys of one comparison and of one copy. */
export const viewTransferLimit = 100

/** The `key` parameters an address may hold, so the whole address stays below about 2,000 characters. */
export const maxKeyQueryLength = 1500

/** The page of the comparison of a selection, below the Compare views tab of Modeling. */
export const compareSelectionPath = (scope: Scope) =>
  `/${scope.org}/${scope.project}/${scope.view}/modeling/compare/selection`

/** The keys without duplicates, in key order. */
export function uniqueKeys(keys: readonly string[]): string[] {
  return [...new Set(keys)].toSorted()
}

/** Whether the keys fit in an address as `key` parameters; a longer list goes by its id (`set`). */
export function fitsInAddress(keys: readonly string[]): boolean {
  // "&key=" and the encoded key
  const length = uniqueKeys(keys).reduce((total, key) => total + encodeURIComponent(key).length + 5, 0)
  return length <= maxKeyQueryLength
}

/** FNV-1a over the text, 32 bits, from the given basis. */
function hash(text: string, basis: number): number {
  let value = basis
  for (let index = 0; index < text.length; index++) {
    value ^= text.charCodeAt(index)
    value = Math.imul(value, 0x01000193)
  }
  return value >>> 0
}

/** The id of a list of keys: the same for the same keys in any order, so the same selection has the same address. */
export function keySetId(keys: readonly string[]): string {
  const text = uniqueKeys(keys).join('\n')
  return `${hash(text, 0x811c9dc5).toString(36)}${hash(text, 0x01000193).toString(36)}`
}

export interface CompareRequest {
  kind: ViewItemKind
  /** Distinct, in key order, at most `viewTransferLimit`. */
  keys: string[]
  /** The target view; undefined when the address names none, or names the view of the route. */
  to?: string
  content: ConfigurationContent
  /** The id of the list the keys came from, when the address has no `key` parameters. */
  set?: string
  /** The address names a list of keys that this browser tab does not have (any more). */
  lost: boolean
  /** The address has more keys than one comparison takes; the first ones are compared. */
  truncated: boolean
}

/**
 * The address of a comparison. The keys go as repeated `key` parameters; give `set`, the id of a remembered list
 * (`useViewTransferStore().remember`), for keys that do not fit (`fitsInAddress`).
 */
export function compareLocation(
  scope: Scope,
  request: { kind: ViewItemKind; keys: readonly string[]; to?: string; content?: ConfigurationContent; set?: string },
): RouteLocationRaw {
  return {
    path: compareSelectionPath(scope),
    query: {
      kind: request.kind,
      ...(request.to ? { to: request.to } : {}),
      ...(request.kind === 'configurations' && request.content === 'effective' ? { content: 'effective' } : {}),
      ...(request.set ? { set: request.set } : { key: uniqueKeys(request.keys) }),
    },
  }
}

const first = (value: LocationQueryValue | LocationQueryValue[] | undefined): string | undefined => {
  const single = Array.isArray(value) ? value[0] : value
  return typeof single === 'string' && single ? single : undefined
}

const all = (value: LocationQueryValue | LocationQueryValue[] | undefined): string[] =>
  (Array.isArray(value) ? value : [value]).filter((item): item is string => typeof item === 'string' && !!item)

/**
 * Reads the address of a comparison. `view` is the view of the route: a `to` that names it is no target, as on
 * Compare views (424-64). `keysOf` answers the keys of a remembered list.
 */
export function parseCompareQuery(
  query: LocationQuery,
  view: string | undefined,
  keysOf: (id: string) => readonly string[] | undefined,
): CompareRequest {
  const kind: ViewItemKind = first(query.kind) === 'annotations' ? 'annotations' : 'configurations'
  const to = first(query.to)
  const set = first(query.set)
  const listed = set ? keysOf(set) : all(query.key)
  const keys = uniqueKeys(listed ?? [])

  return {
    kind,
    keys: keys.slice(0, viewTransferLimit),
    to: to && to !== view ? to : undefined,
    content: kind === 'configurations' && first(query.content) === 'effective' ? 'effective' : 'own',
    set: set && listed ? set : undefined,
    lost: !!set && !listed,
    truncated: keys.length > viewTransferLimit,
  }
}

/** How the documents of a key in the two views relate. */
export type CompareStatus = 'different' | 'identical' | 'onlySource' | 'onlyTarget' | 'absent'

export interface CompareResult {
  status: CompareStatus
  /** Lines that only the source has: what a copy adds to the target. */
  additions: number
  /** Lines that only the target has. */
  deletions: number
  /** The texts that are shown; empty for a side without a document. */
  source: string
  target: string
}

/**
 * Compares the texts of a key in the source and in the target; `undefined` is a side without a document. The target
 * is the old side and the source the new one, as a pull request from the source into the target.
 */
export function compareTexts(source: string | undefined, target: string | undefined): CompareResult {
  if (source === undefined && target === undefined) {
    return { status: 'absent', additions: 0, deletions: 0, source: '', target: '' }
  }
  if (source !== undefined && source === target) {
    return { status: 'identical', additions: 0, deletions: 0, source, target }
  }
  const stats = lineStats(target ?? '', source ?? '')
  const status: CompareStatus = source === undefined ? 'onlyTarget' : target === undefined ? 'onlySource' : 'different'
  return { status, ...stats, source: source ?? '', target: target ?? '' }
}

/** Whether a copy from the source would change the target: the documents differ, or only the source has one. */
export const copyChanges = (result: CompareResult | undefined): boolean =>
  result?.status === 'different' || result?.status === 'onlySource'

/** What the summary of a comparison counts and filters by: the states, and what cuts across them. */
export type CompareFilter = 'all' | CompareStatus | 'deleted' | 'effectiveDiffers' | 'failed'

export const compareFilters: readonly CompareFilter[] = [
  'all',
  'different',
  'onlySource',
  'onlyTarget',
  'identical',
  'absent',
  'deleted',
  'effectiveDiffers',
  'failed',
]

/**
 * The name of a state, on its chip and on the badge of a document. `own` is true for own configurations, whose
 * sides are stored documents: a key without one still has its annotation and its inherited values.
 */
export function compareLabel(
  filter: CompareFilter,
  names: { source: string; target: string; own: boolean },
): string {
  switch (filter) {
    case 'all':
      return 'All'
    case 'different':
      return 'Different'
    case 'identical':
      return 'Identical'
    case 'onlySource':
      return names.own ? `Own configuration only in ${names.source}` : `Only in ${names.source}`
    case 'onlyTarget':
      return names.own ? `Own configuration only in ${names.target}` : `Only in ${names.target}`
    case 'absent':
      return names.own ? 'No own configuration' : 'Not in either view'
    case 'deleted':
      return `Deleted in ${names.target}`
    case 'effectiveDiffers':
      return 'Effective differs'
    case 'failed':
      return 'Failed'
  }
}
