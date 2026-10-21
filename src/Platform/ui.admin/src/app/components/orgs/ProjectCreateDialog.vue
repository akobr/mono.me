<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useForm } from 'vee-validate'
import { createAccessPoint, hasErrorCode, isConflict } from '@42for.net/2splatform.sdk'
import { projectSchema } from '@/forms/names.schema'
import { toTypedSchema } from '@/forms/typed-schema'
import { useSessionStore } from '@/stores/session'
import { errorMessage } from '@/utils/errors'

const props = defineProps<{ org: string }>()
const open = defineModel<boolean>('open', { default: false })

const router = useRouter()
const session = useSessionStore()
const toast = useToast()
const formError = ref<string>()

const { defineField, errors, handleSubmit, isSubmitting, setFieldError, resetForm } = useForm({
  validationSchema: toTypedSchema(projectSchema),
  initialValues: { project: '', description: '' },
})
const [project, projectAttrs] = defineField('project')
const [description, descriptionAttrs] = defineField('description')

const onSubmit = handleSubmit(async (values) => {
  formError.value = undefined

  try {
    await createAccessPoint({
      Organization: props.org,
      Project: values.project,
      ProjectDescription: values.description?.trim() || undefined,
    })
    await session.loadAccount(true)
    open.value = false
    resetForm()
    toast.add({ title: `Project ${values.project} created`, icon: 'i-lucide-folder', color: 'success' })
    await router.push(`/${props.org}/${values.project}/default/annotations`)
  } catch (error) {
    if (isConflict(error)) {
      setFieldError('project', 'A project with this name already exists.')
    } else if (hasErrorCode(error, 'InvalidName')) {
      setFieldError('project', error.message)
    } else {
      formError.value = errorMessage(error)
    }
  }
})
</script>

<template>
  <UModal
    v-model:open="open"
    title="New project"
    :description="`The project is created in ${org}; you become its owner.`"
  >
    <template #body>
      <form class="space-y-4" novalidate @submit="onSubmit">
        <UFormField
          label="Project"
          name="project"
          required
          :error="errors.project"
          help="Lower-case letters, digits and hyphens."
        >
          <UInput v-model="project" v-bind="projectAttrs" class="w-full" autocomplete="off" autofocus />
        </UFormField>
        <UFormField label="Description" name="description" :error="errors.description">
          <UTextarea v-model="description" v-bind="descriptionAttrs" class="w-full" autoresize />
        </UFormField>
        <UAlert v-if="formError" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="formError" />
        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
          <UButton type="submit" label="Create project" :loading="isSubmitting" />
        </div>
      </form>
    </template>
  </UModal>
</template>
