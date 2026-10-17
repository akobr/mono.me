<script setup lang="ts">
import { ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { useForm } from 'vee-validate'
import { z } from 'zod'
import { createView, hasErrorCode, updateView, type View } from '@42for.net/2splatform.sdk'
import { useGetViews } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateByPath } from '@42for.net/2splatform.sdk/vue-query'
import { viewNameSchema } from '@/forms/names.schema'
import { toTypedSchema } from '@/forms/typed-schema'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'

// Views of the project (companion spec E5): registered views with description and creation, discovered views
// (Administrators, ?discover=true) with Register, create, edit description and compare.
const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const canAdmin = useCan('manageAccess')

const views = useGetViews(
  () => context.org ?? '',
  () => context.project ?? '',
  () => ({ discover: canAdmin.value }),
  { query: { enabled: () => !!context.project, meta: { silent: true } } },
)

const creating = ref(false)
const editing = ref<View>()
const description = ref('')
const formError = ref<string>()

const { defineField, errors, handleSubmit, isSubmitting, resetForm, setFieldError } = useForm({
  validationSchema: toTypedSchema(z.object({ name: viewNameSchema, description: z.string().max(500).optional() })),
  initialValues: { name: '', description: '' },
})
const [name, nameAttrs] = defineField('name')
const [newDescription] = defineField('description')

const refresh = () => invalidateByPath(queryClient, `/v1/${context.org}/${context.project}/views`)

const create = handleSubmit(async (values) => {
  formError.value = undefined
  try {
    await createView(context.org!, context.project!, {
      Name: values.name,
      Description: values.description || undefined,
    })
    creating.value = false
    resetForm()
    toast.add({ title: `View ${values.name} created`, color: 'success', icon: 'i-lucide-eye' })
    await refresh()
  } catch (error) {
    if (hasErrorCode(error, 'ViewExists')) {
      setFieldError('name', 'A view with this name already exists.')
    } else if (hasErrorCode(error, 'InvalidName')) {
      setFieldError('name', errorMessage(error))
    } else {
      formError.value = errorMessage(error)
    }
  }
})

async function save(view: View, text: string | undefined) {
  try {
    await updateView(context.org!, context.project!, view.Name!, { Description: text })
    editing.value = undefined
    toast.add({
      title: view.IsRegistered ? 'Description saved' : `View ${view.Name} registered`,
      color: 'success',
      icon: 'i-lucide-check',
    })
    await refresh()
  } catch (error) {
    toast.add({ title: 'The view was not saved', description: errorMessage(error), color: 'error' })
  }
}

function edit(view: View) {
  editing.value = view
  description.value = view.Description ?? ''
}
</script>

<template>
  <UDashboardPanel id="views">
    <template #header>
      <AppNavbar title="Views">
        <template #right>
          <UButton v-if="canAdmin" label="New view" icon="i-lucide-plus" @click="creating = true" />
        </template>
      </AppNavbar>
    </template>
    <template #body>
      <LoadingState v-if="views.isPending.value" />
      <ErrorState v-else-if="views.isError.value" :error="views.error.value" @retry="views.refetch()" />
      <ul v-else class="divide-y divide-default rounded-md border border-default">
        <li v-for="view in views.data.value" :key="view.Name" class="flex flex-wrap items-center gap-3 px-4 py-3">
          <UIcon name="i-lucide-eye" class="size-4 text-muted" />
          <RouterLink
            :to="`/${context.org}/${context.project}/${view.Name}/annotations`"
            class="font-medium hover:underline"
            >{{ view.Name }}</RouterLink
          >
          <UBadge v-if="view.IsDefault" label="production" color="primary" variant="subtle" size="sm" />
          <UBadge v-if="!view.IsRegistered" label="discovered" color="warning" variant="subtle" size="sm" />
          <span class="min-w-0 flex-1 truncate text-sm text-muted">{{ view.Description }}</span>
          <RelativeTime v-if="view.Created" :value="view.Created.At" class="text-xs text-dimmed" />
          <span class="flex gap-1">
            <UButton
              :to="`/${context.org}/${context.project}/${context.view ?? 'default'}/compare?to=${view.Name}`"
              label="Compare"
              size="xs"
              color="neutral"
              variant="ghost"
            />
            <template v-if="canAdmin">
              <UButton
                v-if="!view.IsRegistered"
                label="Register"
                size="xs"
                color="neutral"
                variant="outline"
                @click="save(view, view.Description)"
              />
              <UButton v-else label="Edit" size="xs" color="neutral" variant="ghost" @click="edit(view)" />
            </template>
          </span>
        </li>
      </ul>

      <UModal
        v-model:open="creating"
        title="New view"
        description="A view is a separate catalog of annotations and configurations in the project."
      >
        <template #body>
          <form class="space-y-4" novalidate @submit="create">
            <UFormField label="Name" required :error="errors.name">
              <UInput v-model="name" v-bind="nameAttrs" class="w-full" autofocus autocomplete="off" />
            </UFormField>
            <UFormField label="Description" :error="errors.description">
              <UTextarea v-model="newDescription" class="w-full" autoresize />
            </UFormField>
            <UAlert v-if="formError" color="error" variant="subtle" :description="formError" />
            <div class="flex justify-end gap-2">
              <UButton label="Cancel" color="neutral" variant="ghost" @click="creating = false" />
              <UButton type="submit" label="Create view" :loading="isSubmitting" />
            </div>
          </form>
        </template>
      </UModal>

      <UModal
        :open="!!editing"
        :title="`Edit ${editing?.Name}`"
        @update:open="(value) => !value && (editing = undefined)"
      >
        <template #body>
          <form class="space-y-4" @submit.prevent="save(editing!, description)">
            <UFormField label="Description">
              <UTextarea v-model="description" class="w-full" autoresize autofocus />
            </UFormField>
            <div class="flex justify-end gap-2">
              <UButton label="Cancel" color="neutral" variant="ghost" @click="editing = undefined" />
              <UButton type="submit" label="Save" />
            </div>
          </form>
        </template>
      </UModal>
    </template>
  </UDashboardPanel>
</template>
