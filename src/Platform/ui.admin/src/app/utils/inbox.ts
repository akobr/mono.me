import { hasRole, type ViewInboxItem, type ViewInboxItemLevel } from '@42for.net/2splatform.sdk'

// Presentation of the items of a view's inbox (424-31, docs/Platform/Storyteller/inbox.md). The producer writes the
// title and the text of an item; the page branches on its kind, state, topic, level and job only. Every member of an
// item is optional in the SDK, so each function answers for an item without it.

/** The two lists of the page, which are the values of the `state` of the API's list. */
export type InboxFilter = 'Open' | 'Done'

/** How an item looks: its icon with the class of its color, and the words that say the same to a screen reader. */
export interface InboxAppearance {
  icon: string
  iconClass: string
  label: string
}

// Literal names: the client bundle holds only the icons its scan finds in the sources.
const action: InboxAppearance = { icon: 'i-lucide-sparkles', iconClass: 'text-primary', label: 'Suggested action' }
const message: InboxAppearance = { icon: 'i-lucide-message-square', iconClass: 'text-muted', label: 'Message' }

/** A notification is the result of background work, so its level says how the work ended. */
const notifications: Record<ViewInboxItemLevel, InboxAppearance> = {
  Info: { icon: 'i-lucide-info', iconClass: 'text-info', label: 'Notification' },
  Success: { icon: 'i-lucide-circle-check', iconClass: 'text-success', label: 'Finished' },
  Warning: { icon: 'i-lucide-triangle-alert', iconClass: 'text-warning', label: 'Finished with problems' },
  Error: { icon: 'i-lucide-circle-x', iconClass: 'text-error', label: 'Failed' },
}

/** The icon of an item: of its kind for an action and a message, of its level for a notification. */
export function inboxAppearance(item: Pick<ViewInboxItem, 'Kind' | 'Level'>): InboxAppearance {
  switch (item.Kind) {
    case 'Action':
      return action
    case 'Message':
      return message
    default:
      // a level the page does not know reads as a plain notification
      return notifications[item.Level ?? 'Info'] ?? notifications.Info
  }
}

/** An item only Administrators and Owners see; the others are for every Contributor. */
export const isAdministratorsOnly = (item: Pick<ViewInboxItem, 'MinimalRole'>) =>
  hasRole(item.MinimalRole, 'Administrator')

/** "Accepted" or "Dismissed" for a closed item, before who closed it and when; nothing for an open one. */
export function inboxClosedLabel(item: Pick<ViewInboxItem, 'State'>): 'Accepted' | 'Dismissed' | undefined {
  return item.State === 'Accepted' ? 'Accepted' : item.State === 'Dismissed' ? 'Dismissed' : undefined
}

/** What an item offers; each one is a button of its row. */
export interface InboxOffers {
  /** Accept: an open action. */
  accept: boolean
  /** Dismiss: every open item. */
  dismiss: boolean
  /** Download: the archive of an export that succeeded, open or done. */
  download: boolean
  /** Show the result: the report of an import that ran, open or done. */
  result: boolean
}

/**
 * What an item offers. An item without an id cannot be addressed, so it offers no accept and no dismiss. The archive
 * and the report are read from the item's job, by its routes: an item without a job offers neither, and so does one of
 * the level Error, whose job failed and left nothing to read.
 */
export function inboxOffers(item: ViewInboxItem): InboxOffers {
  const open = item.State === 'Open' && !!item.Id
  const ended = item.Kind === 'Notification' && item.Level !== 'Error' && !!item.Job?.Id

  return {
    accept: open && item.Kind === 'Action',
    dismiss: open,
    download: ended && item.Topic === 'ViewExport' && item.Job?.Kind === 'ViewExport',
    result: ended && item.Topic === 'ViewImport' && item.Job?.Kind === 'ViewImport',
  }
}

/** The confirmation of an acceptance: it names the item and says that its work runs in the background. */
export function inboxAcceptConfirmation(item: Pick<ViewInboxItem, 'Title'>): {
  title: string
  description: string
  confirmLabel: string
} {
  return {
    title: 'Accept this action?',
    description: `${item.Title?.trim() || 'The action'}: the work starts now and runs in the background. Its result arrives in this inbox.`,
    confirmLabel: 'Accept',
  }
}

/**
 * The empty state of a list. Until the platform suggests actions, only the results of exports and imports arrive, and
 * those are for Administrators: a Contributor is told about the actions only.
 */
export function inboxEmptyState(
  filter: InboxFilter,
  administrator: boolean,
): { icon: string; title: string; description: string } {
  if (filter === 'Done') {
    return {
      icon: 'i-lucide-archive',
      title: 'Nothing done yet',
      description: 'Accepted and dismissed items stay here until they expire.',
    }
  }

  return {
    // not the inbox icon of the default: it is the icon of the page in the menu
    icon: 'i-lucide-check-check',
    title: 'Nothing to do',
    description: administrator
      ? 'Results of exports and imports and suggested actions of this view arrive here.'
      : 'Suggested actions of this view arrive here.',
  }
}
