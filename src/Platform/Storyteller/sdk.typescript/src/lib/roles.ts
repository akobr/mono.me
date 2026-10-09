// Account roles on organizations and projects, lowest first (AccountRole of Abstractions.Access).

export type AccountRoleName = 'None' | 'Reader' | 'Contributor' | 'ContributorWithSecrets' | 'Administrator' | 'Owner'

export const roleOrder: readonly AccountRoleName[] = [
  'None',
  'Reader',
  'Contributor',
  'ContributorWithSecrets',
  'Administrator',
  'Owner',
]

/** Position in roleOrder; unknown or missing roles count as None. */
export function roleRank(role: string | null | undefined): number {
  const index = role ? roleOrder.indexOf(role as AccountRoleName) : -1
  return index < 0 ? 0 : index
}

/** True when the actual role is at least the minimal one. */
export function hasRole(actual: string | null | undefined, minimal: AccountRoleName): boolean {
  return roleRank(actual) >= roleRank(minimal)
}

/** The role of an account on an access point ("org" or "org.project") from its AccessMap. */
export function getRole(accessMap: Record<string, string> | null | undefined, accessPointKey: string): AccountRoleName {
  const role = accessMap?.[accessPointKey]
  return role && roleOrder.includes(role as AccountRoleName) ? (role as AccountRoleName) : 'None'
}
