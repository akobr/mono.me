import { ref, shallowRef, watch } from 'vue'
import { findDeletedAnnotations, type Annotation } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { getAllAncestors } from '@/utils/annotation-key'

/** What happens to deleted keys a create would touch (spec 2.4): restore and update them, re-create them, or stop. */
export type DeletedKeyChoice = 'restore' | 'create' | 'cancel'

export interface DeletedKeyCheck {
  /** 'none' when no key is deleted; otherwise the user's choice in DeletedKeysDialog. */
  choice: DeletedKeyChoice | 'none'
  deleted: Annotation[]
}

/**
 * The check before a create (spec 4.5): a write to a deleted key, or below a deleted ancestor, re-creates it empty,
 * so the keys and their ancestors are looked up first. When some are deleted, `check` waits until the user picks
 * an action in DeletedKeysDialog, which binds `open`, `deleted` and `choose`. Closing the dialog cancels.
 */
export function useDeletedKeyCheck() {
  const context = useContextStore()
  const open = ref(false)
  const deleted = shallowRef<Annotation[]>([])
  let resolve: ((choice: DeletedKeyChoice) => void) | undefined

  function choose(choice: DeletedKeyChoice): void {
    const pending = resolve
    resolve = undefined
    open.value = false
    pending?.(choice)
  }

  watch(open, (value) => {
    if (!value && resolve) {
      choose('cancel')
    }
  })

  async function check(keys: string | string[]): Promise<DeletedKeyCheck> {
    const scope = context.scope
    if (!scope) {
      throw new Error('No view is selected.')
    }

    const lookup = [...new Set((Array.isArray(keys) ? keys : [keys]).flatMap((key) => [...getAllAncestors(key), key]))]
    const found: Annotation[] = []
    for (let index = 0; index < lookup.length; index += 100) {
      found.push(
        ...(await findDeletedAnnotations(scope.org, scope.project, scope.view, {
          Keys: lookup.slice(index, index + 100),
        })),
      )
    }

    if (found.length === 0) {
      return { choice: 'none', deleted: [] }
    }

    deleted.value = found
    open.value = true
    const choice = await new Promise<DeletedKeyChoice>((done) => (resolve = done))
    return { choice, deleted: found }
  }

  return { open, deleted, check, choose }
}
