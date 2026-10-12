import type { AnnotationDeletionResult } from '@42for.net/2splatform.sdk'
import { formatDate } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'

/**
 * The toast after a delete (spec 4.4): what moved to Deleted, the date it is purged (PurgeAt of the API), and Undo,
 * which restores the roots of the deletions with their descendants in one request.
 */
export function useDeletionToast() {
  const toast = useToast()
  const actions = useAnnotationActions()

  async function undo(roots: string[], onUndone?: () => void) {
    try {
      const outcome = await actions.restoreMany(roots)
      toast.add(
        outcome.failed.length
          ? {
              title: `${outcome.failed.length} annotations were not restored`,
              description: outcome.failed
                .slice(0, 3)
                .map((failure) => `${failure.Key}: ${failure.Message}`)
                .join(' '),
              color: 'warning',
            }
          : {
              title: roots.length === 1 ? `${roots[0]} restored` : `${roots.length} annotations restored`,
              color: 'success',
              icon: 'i-lucide-undo-2',
            },
      )
      onUndone?.()
    } catch (error) {
      toast.add({ title: 'Undo failed', description: errorMessage(error), color: 'error' })
    }
  }

  return (title: string, deletions: AnnotationDeletionResult[], onUndone?: () => void) => {
    const roots = undoKeys(deletions)
    const total = deletions.reduce((sum, deletion) => sum + (deletion.DeletedKeys?.length ?? 0), 0)
    const descendants = total - roots.length
    const purgeAt = deletions
      .map((deletion) => deletion.Deletion?.PurgeAt)
      .filter((value): value is string => !!value)
      .toSorted()[0]

    const description = [
      descendants > 0 ? `With ${descendants} descendant${descendants === 1 ? '' : 's'}.` : '',
      purgeAt ? `Restorable from Deleted until ${formatDate(new Date(purgeAt))}.` : '',
    ]
      .filter(Boolean)
      .join(' ')

    toast.add({
      title,
      description: description || undefined,
      color: 'success',
      icon: 'i-lucide-trash-2',
      duration: 10_000,
      actions: roots.length
        ? [
            {
              label: 'Undo',
              icon: 'i-lucide-undo-2',
              color: 'neutral',
              variant: 'outline',
              onClick: () => void undo(roots, onUndone),
            },
          ]
        : undefined,
    })
  }
}
