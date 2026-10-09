<script setup lang="ts">
import { useRouter } from 'vue-router'
import { createAccount } from '@42for.net/2splatform.sdk'
import type { OrganizationForm } from '@/forms/names.schema'
import { useSessionStore } from '@/stores/session'

definePage({ meta: { layout: 'bare', allowWithoutAccount: true } })

const router = useRouter()
const session = useSessionStore()

// CreateAccount makes the caller Owner of the new organization and its first project.
async function submit(values: OrganizationForm) {
  session.setAccount(await createAccount({ Organization: values.organization, Project: values.project }))
  await router.replace(`/${values.organization}/${values.project}/default/annotations`)
}
</script>

<template>
  <UCard>
    <div class="space-y-5">
      <div class="space-y-1">
        <h1 class="text-xl font-semibold text-highlighted">
          Welcome{{ session.user ? `, ${session.user.name}` : '' }}
        </h1>
        <p class="text-sm text-muted">
          Create your organization and its first project to start. If a colleague invited you, open the link from the
          invitation email instead.
        </p>
      </div>
      <OrganizationForm :submit="submit" submit-label="Create and continue" />
      <div class="flex justify-center">
        <UButton label="Sign out" color="neutral" variant="link" size="sm" @click="session.signOut()" />
      </div>
    </div>
  </UCard>
</template>
