<script setup lang="ts">
import { useRouter } from 'vue-router'
import { createAccessPoint } from '@42for.net/2splatform.sdk'
import type { OrganizationForm } from '@/forms/names.schema'
import { useSessionStore } from '@/stores/session'

const open = defineModel<boolean>('open', { default: false })
const router = useRouter()
const session = useSessionStore()
const toast = useToast()

async function submit(values: OrganizationForm) {
  await createAccessPoint({
    Organization: values.organization,
    Project: values.project,
    ProjectDescription: values.description?.trim() || undefined,
  })
  await session.loadAccount(true)
  open.value = false
  toast.add({ title: `Organization ${values.organization} created`, icon: 'i-lucide-building-2', color: 'success' })
  await router.push(`/${values.organization}/${values.project}/default/annotations`)
}
</script>

<template>
  <UModal
    v-model:open="open"
    title="New organization"
    description="You become the owner of the organization and its first project."
  >
    <template #body>
      <OrganizationForm :submit="submit" submit-label="Create organization">
        <template #actions>
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
        </template>
      </OrganizationForm>
    </template>
  </UModal>
</template>
