<script setup lang="ts">
import { ref } from 'vue'
import { useForm } from 'vee-validate'
import { hasErrorCode, isConflict } from '@42for.net/2splatform.sdk'
import { organizationSchema, type OrganizationForm } from '@/forms/names.schema'
import { toTypedSchema } from '@/forms/typed-schema'
import { errorMessage } from '@/utils/errors'

/**
 * Organization and first project. The API creates an organization through its first project, so both
 * names are asked for together (onboarding and "New organization"), with the description of the project.
 */
const props = withDefaults(
  defineProps<{ submitLabel?: string; submit: (values: OrganizationForm) => Promise<void> }>(),
  {
    submitLabel: 'Create',
  },
)

const { defineField, errors, handleSubmit, isSubmitting, setFieldError } = useForm({
  validationSchema: toTypedSchema(organizationSchema),
  initialValues: { organization: '', project: '', description: '' },
})

const [organization, organizationAttrs] = defineField('organization')
const [project, projectAttrs] = defineField('project')
const [description, descriptionAttrs] = defineField('description')
const formError = ref<string>()

const onSubmit = handleSubmit(async (values) => {
  formError.value = undefined

  try {
    await props.submit(values)
  } catch (error) {
    if (isConflict(error)) {
      setFieldError('organization', 'This organization already exists, or you are not its owner.')
    } else if (hasErrorCode(error, 'InvalidName')) {
      setFieldError(error.message.includes('project') ? 'project' : 'organization', error.message)
    } else {
      formError.value = errorMessage(error)
    }
  }
})
</script>

<template>
  <form class="space-y-4" novalidate @submit="onSubmit">
    <UFormField
      label="Organization"
      name="organization"
      required
      :error="errors.organization"
      help="Lower-case letters, digits and hyphens, for example acme."
    >
      <UInput v-model="organization" v-bind="organizationAttrs" class="w-full" autocomplete="off" autofocus />
    </UFormField>
    <UFormField
      label="First project"
      name="project"
      required
      :error="errors.project"
      help="For example billing or web-shop."
    >
      <UInput v-model="project" v-bind="projectAttrs" class="w-full" autocomplete="off" />
    </UFormField>
    <UFormField label="Project description" name="description" :error="errors.description">
      <UTextarea v-model="description" v-bind="descriptionAttrs" class="w-full" autoresize />
    </UFormField>
    <UAlert v-if="formError" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="formError" />
    <div class="flex justify-end gap-2">
      <slot name="actions" />
      <UButton type="submit" :label="submitLabel" :loading="isSubmitting" />
    </div>
  </form>
</template>
