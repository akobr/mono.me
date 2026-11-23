import { describe, expect, it } from 'vitest'
import type { ViewInboxItem } from '@42for.net/2splatform.sdk'
import {
  inboxAcceptConfirmation,
  inboxAppearance,
  inboxClosedLabel,
  inboxEmptyState,
  inboxOffers,
  isAdministratorsOnly,
} from '@/utils/inbox'

// What the Inbox page shows of an item and what it offers (424-31): a client branches on the kind, the state, the
// topic, the level and the job of an item, never on its title or text.

const nothing = { accept: false, dismiss: false, download: false, result: false }

const action: ViewInboxItem = {
  Id: '2026112208000000000003f2b8c1d4e5f4a6b9c0d1e2f3a4b5c6d',
  Kind: 'Action',
  Topic: 'SchemaSuggestion',
  Level: 'Info',
  Title: 'Generate a schema for 4 similar configurations',
  MinimalRole: 'Contributor',
  State: 'Open',
  Action: 'GenerateSchema',
}

const exported: ViewInboxItem = {
  Id: '20261122091407000000079aab47ca406989d11506daca106041b',
  Kind: 'Notification',
  Topic: 'ViewExport',
  Level: 'Success',
  Title: 'Export finished',
  MinimalRole: 'Administrator',
  State: 'Open',
  Job: { Kind: 'ViewExport', Id: '5f0c2d7e9a414b6c8e3d1f2a4b6c8d0e', FileName: 'export-acme-billing-default.zip' },
}

const imported: ViewInboxItem = {
  Id: '20261122093012000000061c5e0d2a7b94f3e8a1d0c6b5f4e3d2c',
  Kind: 'Notification',
  Topic: 'ViewImport',
  Level: 'Warning',
  Title: 'Import finished with failures',
  MinimalRole: 'Administrator',
  State: 'Open',
  Job: { Kind: 'ViewImport', Id: '9b1d4c7a2e5f4083a6c9d2e5f8b1a4c7', FileName: 'acme.zip' },
}

const message: ViewInboxItem = { ...action, Kind: 'Message', Action: undefined, Title: 'Schema suggestions are on' }

describe('what an inbox item offers', () => {
  it('offers Accept and Dismiss for an open action', () => {
    expect(inboxOffers(action)).toEqual({ ...nothing, accept: true, dismiss: true })
  })

  it.each(['Accepted', 'Dismissed'] as const)('offers nothing for an action that was %s', (State) => {
    expect(inboxOffers({ ...action, State })).toEqual(nothing)
  })

  it('offers only Dismiss for an open message', () => {
    expect(inboxOffers(message)).toEqual({ ...nothing, dismiss: true })
  })

  it('offers Download for an export that succeeded, open or done', () => {
    expect(inboxOffers(exported)).toEqual({ ...nothing, dismiss: true, download: true })
    expect(inboxOffers({ ...exported, State: 'Dismissed' })).toEqual({ ...nothing, download: true })
  })

  it.each(['Success', 'Warning'] as const)(
    'offers Show the result for an import that ran with the level %s, open or done',
    (Level) => {
      expect(inboxOffers({ ...imported, Level })).toEqual({ ...nothing, dismiss: true, result: true })
      expect(inboxOffers({ ...imported, Level, State: 'Dismissed' })).toEqual({ ...nothing, result: true })
    },
  )

  // a failed job left no archive and no report: its whole content is the error text
  it.each([
    ['export', exported],
    ['import', imported],
  ] as const)('offers nothing that needs the job of a failed %s', (_, item) => {
    expect(inboxOffers({ ...item, Level: 'Error' })).toEqual({ ...nothing, dismiss: true })
    expect(inboxOffers({ ...item, Level: 'Error', State: 'Dismissed' })).toEqual(nothing)
  })

  it.each([
    ['without a job', { Job: undefined }],
    ['whose job has no id', { Job: { Kind: 'ViewExport', FileName: 'export.zip' } }],
    ['whose job is of another kind', { Job: { ...imported.Job } }],
    ['of another topic', { Topic: 'SchemaSuggestion' }],
  ] as const)('offers no Download for a result %s', (_, change) => {
    expect(inboxOffers({ ...exported, ...change })).toEqual({ ...nothing, dismiss: true })
  })

  it('offers no result for a finished import without a job, as the seeded one of the mock', () => {
    expect(inboxOffers({ ...imported, Level: 'Success', State: 'Dismissed', Job: undefined })).toEqual(nothing)
  })

  // an accepted action may carry the job its acceptance started; its result arrives as a notification of its own
  it('offers no Download for an action that names an export', () => {
    expect(inboxOffers({ ...action, State: 'Accepted', Topic: 'ViewExport', Job: exported.Job })).toEqual(nothing)
  })

  it('offers neither Accept nor Dismiss for an item without an id, which no route can address', () => {
    expect(inboxOffers({ ...action, Id: undefined })).toEqual(nothing)
    expect(inboxOffers({ ...exported, Id: undefined })).toEqual({ ...nothing, download: true })
  })

  it('offers nothing for an item without any member', () => {
    expect(inboxOffers({})).toEqual(nothing)
  })
})

describe('how an inbox item looks', () => {
  it('shows an action with the sparkles and a message with the speech bubble, whatever their level', () => {
    expect(inboxAppearance({ Kind: 'Action', Level: 'Error' })).toEqual({
      icon: 'i-lucide-sparkles',
      iconClass: 'text-primary',
      label: 'Suggested action',
    })
    expect(inboxAppearance({ Kind: 'Message', Level: 'Warning' })).toEqual({
      icon: 'i-lucide-message-square',
      iconClass: 'text-muted',
      label: 'Message',
    })
  })

  it.each([
    ['Info', 'i-lucide-info', 'text-info', 'Notification'],
    ['Success', 'i-lucide-circle-check', 'text-success', 'Finished'],
    ['Warning', 'i-lucide-triangle-alert', 'text-warning', 'Finished with problems'],
    ['Error', 'i-lucide-circle-x', 'text-error', 'Failed'],
  ] as const)('shows a notification of the level %s with %s', (Level, icon, iconClass, label) => {
    expect(inboxAppearance({ Kind: 'Notification', Level })).toEqual({ icon, iconClass, label })
  })

  it('shows an item without a kind or with a level it does not know as a plain notification', () => {
    const plain = inboxAppearance({ Kind: 'Notification', Level: 'Info' })

    expect(inboxAppearance({})).toEqual(plain)
    expect(inboxAppearance({ Kind: 'Notification', Level: 'Critical' as never })).toEqual(plain)
  })

  it.each([
    ['Contributor', false],
    ['ContributorWithSecrets', false],
    ['Administrator', true],
    ['Owner', true],
  ] as const)('tells that an item for %s is for Administrators only: %s', (MinimalRole, expected) => {
    expect(isAdministratorsOnly({ MinimalRole })).toBe(expected)
  })

  it('does not call an item without a role one for Administrators only', () => {
    expect(isAdministratorsOnly({})).toBe(false)
  })

  it('names how an item was closed, and nothing for an open one', () => {
    expect(inboxClosedLabel({ State: 'Accepted' })).toBe('Accepted')
    expect(inboxClosedLabel({ State: 'Dismissed' })).toBe('Dismissed')
    expect(inboxClosedLabel({ State: 'Open' })).toBeUndefined()
    expect(inboxClosedLabel({})).toBeUndefined()
  })
})

describe('the texts of the inbox', () => {
  it('names the action in the confirmation of its acceptance, and that the work runs in the background', () => {
    expect(inboxAcceptConfirmation(action)).toEqual({
      title: 'Accept this action?',
      description:
        'Generate a schema for 4 similar configurations: the work starts now and runs in the background. Its result arrives in this inbox.',
      confirmLabel: 'Accept',
    })
    expect(inboxAcceptConfirmation({}).description).toMatch(/^The action: the work starts now/)
  })

  it('tells an Administrator that the results of exports and imports arrive in the inbox too', () => {
    expect(inboxEmptyState('Open', true)).toEqual({
      icon: 'i-lucide-check-check',
      title: 'Nothing to do',
      description: 'Results of exports and imports and suggested actions of this view arrive here.',
    })
  })

  it('tells a Contributor only about the suggested actions', () => {
    expect(inboxEmptyState('Open', false)).toEqual({
      icon: 'i-lucide-check-check',
      title: 'Nothing to do',
      description: 'Suggested actions of this view arrive here.',
    })
  })

  it.each([true, false])('says Nothing done yet under Done, whatever the role (Administrator: %s)', (administrator) => {
    expect(inboxEmptyState('Done', administrator)).toMatchObject({ title: 'Nothing done yet' })
    // the icon of the page in the menu is the default of an empty state: neither list uses it
    expect(inboxEmptyState('Done', administrator).icon).not.toBe('i-lucide-inbox')
  })
})
