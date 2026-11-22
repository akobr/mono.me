<script setup lang="ts">
import { computed, ref } from 'vue'
import type { AccountRoleName } from '@42for.net/2splatform.sdk'
import { useLeaveProject } from '@/composables/useLeaveProject'
import { roleLabel } from '@/utils/roles'

/**
 * "Leave project" in the Project card of Settings (424-271), for every member of the project: the API lets any member
 * leave without a recent sign-in, but the last Owner. The confirmation says what goes and how to come back.
 */
const props = defineProps<{
  projectKey: string
  role: AccountRoleName
  /** The caller is the project's only Owner: the button is disabled, and the text says why. */
  lastOwner?: boolean
  /** The project is the deployment's exhibition, which the user can join again on Organizations. */
  exhibition?: boolean
}>()

const { leaving, leave } = useLeaveProject()
const confirming = ref(false)

const description = computed(
  () =>
    `You lose your ${roleLabel(props.role)} role on ${props.projectKey}. ${
      props.exhibition
        ? 'You can join the exhibition again from Organizations.'
        : 'To come back, you need a new invitation.'
    }`,
)

async function confirm() {
  // for the project of the click: the dialog may stay open while the context switcher moves on
  const projectKey = props.projectKey
  await leave(projectKey)
  confirming.value = false
}
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-3">
    <p class="text-sm text-muted">
      {{
        lastOwner
          ? 'You are the only Owner of this project. Make another member an Owner before you leave.'
          : 'Leave the project to remove it from your organizations.'
      }}
    </p>
    <UButton
      label="Leave project"
      icon="i-lucide-log-out"
      color="error"
      variant="outline"
      :disabled="lastOwner"
      @click="confirming = true"
    />

    <ConfirmDialog
      v-model:open="confirming"
      :title="`Leave ${projectKey}?`"
      :description="description"
      confirm-label="Leave project"
      :loading="leaving"
      @confirm="confirm"
    />
  </div>
</template>
