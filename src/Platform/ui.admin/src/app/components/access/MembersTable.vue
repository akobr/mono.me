<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  getRole,
  hasErrorCode,
  removeMember,
  setMemberRole,
  type AccessPointMember,
  type AccountRoleName,
} from '@42for.net/2splatform.sdk'
import { useGetMembers } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateAccess } from '@/queries/invalidation'
import { useSessionStore } from '@/stores/session'
import { errorMessage } from '@/utils/errors'
import { memberRules } from '@/utils/roles'

/**
 * Members of an organization or project (companion spec E2): name, user name or email, role and remove. Role
 * changes set the exact role (`PUT …/members/{accountId}`); the member rules are checked here and by the API.
 */
const props = withDefaults(defineProps<{ accessPointKey: string; highlight?: string }>(), { highlight: undefined })

const session = useSessionStore()
const queryClient = useQueryClient()
const toast = useToast()

const members = useGetMembers(() => props.accessPointKey, { query: { meta: { silent: true } } })
const callerRole = computed<AccountRoleName>(() => getRole(session.accessMap, props.accessPointKey))
const removing = ref<AccessPointMember>()
const busy = ref<string>()

const rows = computed(() =>
  (members.data.value ?? [])
    .toSorted((a, b) => (a.Name ?? a.AccountId ?? '').localeCompare(b.Name ?? b.AccountId ?? ''))
    .map((member) => ({
      member,
      rules: memberRules(member, members.data.value ?? [], session.account?.Id, callerRole.value),
    })),
)

// ?highlight= of a history entry's actor: the row is marked and scrolled into view once the members are loaded.
watch(
  () => [props.highlight, members.data.value] as const,
  async ([highlight, data]) => {
    if (highlight && data?.some((member) => member.AccountId === highlight)) {
      await nextTick()
      document.getElementById(`member-${highlight}`)?.scrollIntoView({ block: 'center' })
    }
  },
  { immediate: true },
)

const messages: Record<string, string> = {
  LastOwner: 'The last Owner cannot be demoted or removed.',
  ElevatedRole: 'Only Owners can grant or revoke Owner.',
  SelfRoleChange: 'You cannot change your own role.',
  MemberNotFound: 'The member no longer exists.',
}

const describe = (error: unknown) =>
  Object.entries(messages).find(([code]) => hasErrorCode(error, code))?.[1] ?? errorMessage(error)

async function refresh(member: AccessPointMember) {
  await invalidateAccess(queryClient, props.accessPointKey)
  if (member.AccountId === session.account?.Id) {
    await session.loadAccount(true)
  }
}

async function changeRole(member: AccessPointMember, role: AccountRoleName) {
  busy.value = member.AccountId
  try {
    await setMemberRole(props.accessPointKey, member.AccountId!, { Role: role })
    toast.add({
      title: `${member.Name ?? member.AccountId} is now ${role}`,
      color: 'success',
      icon: 'i-lucide-user-check',
    })
    await refresh(member)
  } catch (error) {
    toast.add({ title: 'The role was not changed', description: describe(error), color: 'error' })
  } finally {
    busy.value = undefined
  }
}

async function remove() {
  const member = removing.value
  if (!member) {
    return
  }
  busy.value = member.AccountId
  try {
    await removeMember(props.accessPointKey, member.AccountId!)
    removing.value = undefined
    toast.add({ title: `${member.Name ?? member.AccountId} removed`, color: 'success', icon: 'i-lucide-user-minus' })
    await refresh(member)
  } catch (error) {
    toast.add({ title: 'The member was not removed', description: describe(error), color: 'error' })
  } finally {
    busy.value = undefined
  }
}
</script>

<template>
  <div class="space-y-2">
    <LoadingState v-if="members.isPending.value" />
    <ErrorState v-else-if="members.isError.value" :error="members.error.value" @retry="members.refetch()" />
    <div v-else class="overflow-x-auto rounded-md border border-default">
      <table class="w-full text-sm">
        <thead class="bg-elevated/50 text-xs text-muted">
          <tr>
            <th class="px-3 py-2 text-start font-medium">Name</th>
            <th class="px-3 py-2 text-start font-medium">User</th>
            <th class="px-3 py-2 text-start font-medium">Role</th>
            <th class="w-10 px-3 py-2" />
          </tr>
        </thead>
        <tbody class="divide-y divide-default">
          <tr
            v-for="{ member, rules } in rows"
            :id="`member-${member.AccountId}`"
            :key="member.AccountId"
            :class="member.AccountId === highlight ? 'bg-primary/10' : undefined"
            :aria-current="member.AccountId === highlight ? 'true' : undefined"
          >
            <td class="px-3 py-2">
              <span class="flex items-center gap-2">
                <UAvatar :alt="member.Name ?? member.AccountId" size="2xs" />
                <span class="font-medium">{{ member.Name ?? '–' }}</span>
                <UBadge
                  v-if="member.AccountId === session.account?.Id"
                  label="you"
                  color="neutral"
                  variant="outline"
                  size="sm"
                />
              </span>
            </td>
            <td class="px-3 py-2 text-muted">
              <span>{{ member.UserName ?? '' }}</span>
              <span class="block font-mono text-xs text-dimmed">{{ member.AccountId }}</span>
            </td>
            <td class="px-3 py-2">
              <UTooltip :text="rules.reason" :disabled="rules.canChange">
                <USelect
                  :model-value="member.Role"
                  :items="rules.canChange ? rules.roles : [member.Role!]"
                  :disabled="!rules.canChange || busy === member.AccountId"
                  size="sm"
                  class="w-48"
                  :aria-label="`Role of ${member.Name ?? member.AccountId}`"
                  @update:model-value="(role) => role !== member.Role && changeRole(member, role as AccountRoleName)"
                />
              </UTooltip>
            </td>
            <td class="px-3 py-2 text-end">
              <UTooltip :text="rules.reason ?? 'Remove'">
                <UButton
                  icon="i-lucide-user-minus"
                  color="neutral"
                  variant="ghost"
                  size="sm"
                  :aria-label="`Remove ${member.Name ?? member.AccountId}`"
                  :disabled="!rules.canRemove"
                  @click="removing = member"
                />
              </UTooltip>
            </td>
          </tr>
          <tr v-if="rows.length === 0">
            <td colspan="4" class="px-3 py-4 text-muted">No members.</td>
          </tr>
        </tbody>
      </table>
    </div>

    <ConfirmDialog
      :open="!!removing"
      :title="`Remove ${removing?.Name ?? removing?.AccountId}?`"
      :description="`They lose their ${removing?.Role} role on ${accessPointKey}.`"
      confirm-label="Remove"
      :loading="!!busy"
      @update:open="(open) => !open && (removing = undefined)"
      @confirm="remove"
    />
  </div>
</template>
