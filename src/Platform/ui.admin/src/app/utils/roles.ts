import { hasRole, roleOrder, type AccessPointMember, type AccountRoleName } from '@42for.net/2splatform.sdk'

/** What each role can do (spec section 13), for the role legend. */
export const roleDescriptions: Record<AccountRoleName, string> = {
  None: 'No access.',
  Reader: 'Reads annotations, configurations, templates and schemas.',
  Contributor: 'Edits annotations, configurations, templates and schemas; manages machines.',
  ContributorWithSecrets: 'Like Contributor, and reads resolved configurations with secrets.',
  Administrator: 'Manages members, invitations, shared certificates, the machine policy and views.',
  Owner: 'Everything, including granting Owner and creating projects in the organization.',
}

export type AssignableRole = Exclude<AccountRoleName, 'None'>

export const assignableRoles = roleOrder.filter((role): role is AssignableRole => role !== 'None')

export interface MemberRules {
  /** Roles the caller may set on this member. */
  roles: AssignableRole[]
  canChange: boolean
  canRemove: boolean
  /** Why the member cannot be changed (tooltip). */
  reason?: string
}

/**
 * The member rules (spec section 13), enforced in the UI and again by the API:
 * - only Owners can grant or revoke Owner;
 * - the last Owner cannot be demoted or removed;
 * - Administrators cannot change their own role.
 */
export function memberRules(
  member: AccessPointMember,
  members: readonly AccessPointMember[],
  callerId: string | undefined,
  callerRole: AccountRoleName,
): MemberRules {
  const owner = callerRole === 'Owner'
  const self = !!callerId && member.AccountId === callerId
  const lastOwner = member.Role === 'Owner' && members.filter((entry) => entry.Role === 'Owner').length <= 1

  if (!hasRole(callerRole, 'Administrator')) {
    return { roles: [], canChange: false, canRemove: false, reason: 'Requires the Administrator role' }
  }
  if (lastOwner) {
    return {
      roles: ['Owner'],
      canChange: false,
      canRemove: false,
      reason: 'The last Owner cannot be demoted or removed',
    }
  }
  if (self && !owner) {
    return { roles: [], canChange: false, canRemove: false, reason: 'Administrators cannot change their own role' }
  }
  if (member.Role === 'Owner' && !owner) {
    return { roles: [], canChange: false, canRemove: false, reason: 'Only Owners can change an Owner' }
  }

  return {
    roles: assignableRoles.filter((role) => owner || role !== 'Owner'),
    canChange: true,
    canRemove: true,
  }
}
