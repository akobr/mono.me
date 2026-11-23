import { defineStore } from 'pinia'
import { keySetId, uniqueKeys } from '@/utils/view-compare'
import { onSignOut } from './session'

const storageKey = '2s-admin:view-transfer'

/** The lists of keys the tab keeps: a comparison in Modeling's memory or in the history still finds its own. */
const maxLists = 8

/**
 * What the comparison and the copy between views keep outside their address (spec "Copy and Compare Annotations and
 * Configurations Between Views", section 3):
 * - the keys of a selection that is too long for an address, by an id that the address carries (`set`). They live
 *   in sessionStorage, so a reload of the tab finds them; another tab, another browser or a sign-out does not;
 * - the target view last used in a project, in memory only, to preselect it.
 */
export const useViewTransferStore = defineStore('view-transfer', () => {
  /** In insertion order: the first list is the oldest. */
  let lists: Map<string, readonly string[]> | undefined
  const targets = new Map<string, string>()

  function read(): Map<string, readonly string[]> {
    try {
      const saved = JSON.parse(sessionStorage.getItem(storageKey) ?? '[]') as unknown
      return new Map(
        Array.isArray(saved)
          ? saved.filter(
              (entry): entry is [string, string[]] =>
                Array.isArray(entry) &&
                typeof entry[0] === 'string' &&
                Array.isArray(entry[1]) &&
                entry[1].every((key: unknown) => typeof key === 'string'),
            )
          : [],
      )
    } catch {
      return new Map()
    }
  }

  function save(): void {
    try {
      if (lists?.size) {
        sessionStorage.setItem(storageKey, JSON.stringify([...lists]))
      } else {
        sessionStorage.removeItem(storageKey)
      }
    } catch {
      // storage can be disabled or full: the list then lasts until the page is reloaded
    }
  }

  /**
   * Keeps the keys and returns their id, a hash of the sorted keys: the same selection has the same id, and keeping
   * it again makes it the newest. The oldest of more than eight lists is dropped.
   */
  function remember(keys: readonly string[]): string {
    lists ??= read()
    const id = keySetId(keys)
    lists.delete(id)
    lists.set(id, uniqueKeys(keys))
    while (lists.size > maxLists) {
      lists.delete(lists.keys().next().value!)
    }
    save()
    return id
  }

  function keysOf(id: string): readonly string[] | undefined {
    lists ??= read()
    return lists.get(id)
  }

  /** The target last compared with or copied to in a project, by `org.project`. */
  function lastTarget(projectKey: string): string | undefined {
    return targets.get(projectKey)
  }

  function rememberTarget(projectKey: string, view: string): void {
    targets.set(projectKey, view)
  }

  function clear(): void {
    lists = new Map()
    targets.clear()
    save()
  }

  return { remember, keysOf, lastTarget, rememberTarget, clear }
})

// The next user never sees which keys the previous one compared.
onSignOut(() => useViewTransferStore().clear())
