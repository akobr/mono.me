import type { AuditActor } from '@42for.net/2splatform.sdk'
import type { StoredDocument } from '../documents'
import { deepMerge } from '../merge'
import { people, type PersonaId } from './people'

// Version histories of the showcase documents, with the rule of documents.ts: a version keeps its content for 30
// days, older versions show only who wrote them and when.

const day = 86_400_000
const contentDays = 30

export interface HistoryOptions {
  /** Days between two versions; 5 by default. */
  every?: number
  /** Age of the newest version in days; 1 by default. */
  lastDaysAgo?: number
  /** The authors of the versions, cycled from the first; the project's editors by default. */
  authors?: readonly PersonaId[]
}

export const personActor = (persona: PersonaId): AuditActor => ({
  Kind: 'Account',
  Id: people[persona].id,
  Name: people[persona].name,
})

/** One document from its contents, oldest first. */
export function versionHistory(
  contents: readonly Record<string, unknown>[],
  options: HistoryOptions & { authors: readonly PersonaId[] },
  now = Date.now(),
): StoredDocument {
  const every = options.every ?? 5
  const last = options.lastDaysAgo ?? 1
  return {
    versions: contents.map((content, index) => {
      const version = index + 1
      const created = now - ((contents.length - version) * every + last) * day
      return {
        Version: version,
        Modified: {
          At: new Date(created).toISOString(),
          By: personActor(options.authors[index % options.authors.length]!),
        },
        ExpirationTime: new Date(created + contentDays * day).toISOString(),
        Content: now - created > contentDays * day ? undefined : structuredClone(content),
      }
    }),
  }
}

/**
 * The contents of a long history: `base`, then each next version is the previous one deep-merged with the patch of
 * `step` (arrays are replaced, as in the API's merge).
 */
export function evolve(
  base: Record<string, unknown>,
  count: number,
  step: (version: number, previous: Record<string, unknown>) => Record<string, unknown>,
): Record<string, unknown>[] {
  const contents = [structuredClone(base)]
  for (let version = 2; version <= count; version++) {
    const previous = contents.at(-1)!
    contents.push(deepMerge(previous, step(version, previous)) as Record<string, unknown>)
  }
  return contents
}
