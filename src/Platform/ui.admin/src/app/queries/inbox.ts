import { toValue, type MaybeRefOrGetter } from 'vue'
import type { QueryClient } from '@tanstack/vue-query'
import { dismissViewInboxItem, getViewInbox, type ViewInboxItem } from '@42for.net/2splatform.sdk'
import { invalidateByPath, useGetViewInbox } from '@42for.net/2splatform.sdk/vue-query'
import type { Scope } from '@/stores/context'
import type { InboxFilter } from '@/utils/inbox'

// The inbox of a view (424-31, docs/Platform/Storyteller/inbox.md): its first page as a query, for the Inbox page and
// for the mark of the menu, and what keeps it fresh after a change.

/** While the Inbox page is open, it reads its open items again this often. */
export const inboxPollMs = 20_000

/** How long the item of a job that just ended is waited for, before it is looked for once more. */
export const jobItemRetryMs = 2000

/**
 * How long an answer counts as fresh: a caller that mounts later reads the list again. So the Inbox page opens with
 * the answer of the menu when that is this new, and with a fresh one otherwise.
 */
export const inboxStaleMs = 15_000

const inboxPath = (scope: Scope) => `/v1/${scope.org}/${scope.project}/${scope.view}/inbox`

const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * The first page of the inbox of a view: its open items, or the accepted and dismissed ones. The `state` is always
 * sent, so every caller of one list shares one query: the sidebar and the Inbox page read the open items with the same
 * key, and with the same options, because a query keeps the options of the observer that set them last. A failed read
 * is silent: the page shows it in place, and the menu shows no mark.
 *
 * `enabled` asks only when it is true (and the scope is complete), so nothing is requested below the Contributor role.
 * `poll` reads the list again every 20 seconds while the caller is mounted, and stops while the query is in error.
 */
export function useViewInbox(
  scope: MaybeRefOrGetter<Partial<Scope> | undefined>,
  filter: MaybeRefOrGetter<InboxFilter>,
  options: { enabled?: MaybeRefOrGetter<boolean>; poll?: boolean } = {},
) {
  const part = (name: keyof Scope) => () => toValue(scope)?.[name] ?? ''
  const isComplete = () => {
    const { org, project, view } = toValue(scope) ?? {}
    return !!org && !!project && !!view
  }

  return useGetViewInbox(part('org'), part('project'), part('view'), () => ({ state: toValue(filter) }), {
    query: {
      enabled: () => isComplete() && toValue(options.enabled ?? true),
      staleTime: inboxStaleMs,
      refetchInterval: options.poll ? (query) => (query.state.status === 'error' ? false : inboxPollMs) : false,
      meta: { silent: true },
    },
  })
}

/** Both lists of the inbox of a view, and with them the count of the Open tab and the mark of the menu. */
export function invalidateInbox(client: QueryClient, scope: Scope): Promise<void> {
  return invalidateByPath(client, inboxPath(scope))
}

/** The open notification of a job, among the newest open items of the view. */
async function findJobItem(scope: Scope, jobId: string): Promise<ViewInboxItem | undefined> {
  const page = await getViewInbox(scope.org, scope.project, scope.view, { state: 'Open' satisfies InboxFilter })
  return page.Items?.find((item) => item.Kind === 'Notification' && item.Job?.Id === jobId && !!item.Id)
}

/**
 * Dismisses the notification of a job whose end this tab already showed (spec 7.4), so the inbox does not report it a
 * second time: the item is then under Done, dismissed by this person, with its Download or its result. The back end
 * pushes the item right after it saves the end of the job, so an item that is not there yet is looked for once more
 * after two seconds. Every failure is ignored: without the dismissal the item only stays open. The lists of the view
 * are read again afterwards, whatever happened.
 */
export async function dismissJobItem(client: QueryClient, scope: Scope, jobId: string): Promise<void> {
  try {
    let item = await findJobItem(scope, jobId)
    if (!item) {
      await wait(jobItemRetryMs)
      item = await findJobItem(scope, jobId)
    }
    if (item?.Id) {
      await dismissViewInboxItem(scope.org, scope.project, scope.view, item.Id)
    }
  } catch {
    // no inbox routes in this deployment, a lost role, an item somebody closed meanwhile: nothing to tell the user
  }

  await invalidateInbox(client, scope)
}
