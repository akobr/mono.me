import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { getRole, hasRole, type AccountRoleName } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { useSessionStore } from '@/stores/session'

/** Minimum role per capability (spec section 13); the API enforces the same rules. */
export const capabilityRoles = {
  read: 'Reader',
  edit: 'Contributor',
  machines: 'Contributor',
  secrets: 'ContributorWithSecrets',
  manageAccess: 'Administrator',
  owner: 'Owner',
} as const satisfies Record<string, AccountRoleName>

export type Capability = keyof typeof capabilityRoles

export type RoleScope = 'project' | 'org'

/** The caller's role in the current project and organization, read from Account.AccessMap. */
export function useRole() {
  const session = useSessionStore()
  const context = useContextStore()

  const projectRole = computed<AccountRoleName>(() =>
    context.projectKey ? getRole(session.accessMap, context.projectKey) : 'None',
  )
  const orgRole = computed<AccountRoleName>(() => (context.org ? getRole(session.accessMap, context.org) : 'None'))

  function roleIn(scope: RoleScope): AccountRoleName {
    return scope === 'project' ? projectRole.value : orgRole.value
  }

  function can(capability: Capability | AccountRoleName, scope: RoleScope = 'project'): boolean {
    const minimal =
      capability in capabilityRoles ? capabilityRoles[capability as Capability] : (capability as AccountRoleName)
    return hasRole(roleIn(scope), minimal)
  }

  return { projectRole, orgRole, roleIn, can }
}

/** Reactive check for one capability, for templates: `const canEdit = useCan('edit')`. */
export function useCan(capability: MaybeRefOrGetter<Capability | AccountRoleName>, scope: RoleScope = 'project') {
  const role = useRole()
  return computed(() => role.can(toValue(capability), scope))
}
