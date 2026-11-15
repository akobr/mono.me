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
  defineProps<{
    submitLabel?: string
    submit: (values: OrganizationForm) => Promise<void>
    /** The first field takes the focus when the form shows; the onboarding wizard moves the focus itself. */
    autofocus?: boolean
    /** No submit, while the page has another request in flight. */
    disabled?: boolean
    /**
     * A field is checked when the user leaves it. The onboarding wizard turns this off: it has other ways out below
     * the form, and the error of an untouched field would move them under the pointer of whoever takes one.
     */
    validateOnBlur?: boolean
  }>(),
  {
    submitLabel: 'Create',
    autofocus: true,
    validateOnBlur: true,
  },
)

const { defineField, errors, handleSubmit, isSubmitting, setFieldError } = useForm({
  validationSchema: toTypedSchema(organizationSchema),
  initialValues: { organization: '', project: '', description: '' },
})

// A change and the submit still check the fields without the check on blur.
const fieldConfig = () => ({ validateOnBlur: props.validateOnBlur })
const [organization, organizationAttrs] = defineField('organization', fieldConfig)
const [project, projectAttrs] = defineField('project', fieldConfig)
const [description, descriptionAttrs] = defineField('description', fieldConfig)
const formError = ref<string>()

const submitValues = handleSubmit(async (values) => {
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

// The disabled button stops a click and Enter; this stops a submit that comes any other way.
function onSubmit(event: Event) {
  if (props.disabled) {
    event.preventDefault()
    return
  }
  return submitValues(event)
}
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
      <UInput
        v-model="organization"
        v-bind="organizationAttrs"
        class="w-full"
        autocomplete="off"
        :autofocus="autofocus"
      />
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
      <UButton type="submit" :label="submitLabel" :loading="isSubmitting" :disabled="disabled" />
    </div>
  </form>
</template>
