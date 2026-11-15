import { describe, expect, it } from 'vitest'
import { AccessAuditEntryAction } from '@42for.net/2splatform.sdk'
import {
  accessAuditActionLabel,
  accessAuditActions,
  accessAuditEntryLabel,
  accessAuditProject,
  isLongAuditValue,
  isRoleValue,
  readableAuditValue,
} from '@/utils/access-audit'

// The labels and values of the organization's access audit (424-263).
describe('access audit labels', () => {
  it('names every action of the API in words, in the order of its enum', () => {
    expect(accessAuditActions).toEqual(Object.values(AccessAuditEntryAction))
    const labels = accessAuditActions.map(accessAuditActionLabel)
    expect(labels.every((label, index) => label !== accessAuditActions[index])).toBe(true)
    expect(new Set(labels).size).toBe(labels.length)
  })

  it('keeps the name of an action it does not know', () => {
    expect(accessAuditActionLabel('MemberSuspended')).toBe('MemberSuspended')
  })

  it('names what a created access point is', () => {
    expect(accessAuditEntryLabel({ Action: 'AccessPointCreated', AccessPointKey: 'acme' })).toBe('Organization created')
    expect(accessAuditEntryLabel({ Action: 'AccessPointCreated', AccessPointKey: 'acme.billing' })).toBe(
      'Project created',
    )
    expect(accessAuditEntryLabel({ Action: 'MemberJoined', AccessPointKey: 'acme' })).toBe('Member joined')
  })

  it('reads the project of an access point key', () => {
    expect(accessAuditProject('acme.billing')).toBe('billing')
    expect(accessAuditProject('acme')).toBeUndefined()
    expect(accessAuditProject(undefined)).toBeUndefined()
  })
})

describe('access audit values', () => {
  it('tells a role from any other value', () => {
    expect(isRoleValue('ContributorWithSecrets')).toBe(true)
    expect(isRoleValue('ApiKey')).toBe(false)
    expect(isRoleValue(undefined)).toBe(false)
  })

  it('indents JSON and leaves other values and a cut JSON value as they are', () => {
    expect(readableAuditValue('{"Scopes":["AnnotationRead"]}')).toBe('{\n  "Scopes": [\n    "AnnotationRead"\n  ]\n}')
    expect(readableAuditValue('Billing.')).toBe('Billing.')
    const cut = '{"Rules":[{"Issuer":"github"… (truncated, sha256 0123456789abcdef)'
    expect(readableAuditValue(cut)).toBe(cut)
  })

  it('opens a long value or JSON in full, not a role or a short text', () => {
    expect(isLongAuditValue('Owner')).toBe(false)
    expect(isLongAuditValue('ApiKey')).toBe(false)
    expect(isLongAuditValue('[]')).toBe(false)
    expect(isLongAuditValue('{"CredentialKind":"ApiKey"}')).toBe(true)
    expect(isLongAuditValue('A description that is longer than one short line of the table.')).toBe(true)
  })
})
