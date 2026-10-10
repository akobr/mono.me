<script setup lang="ts">
import { computed, ref } from 'vue'
import type { ControlElement } from '@jsonforms/core'
import { rendererProps, useJsonFormsControl } from '@jsonforms/vue'

// String, multiline, secret and URL controls (spec section 10). An empty input removes the value, so an inherited
// value applies again.
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)

const schema = computed(() => control.value.schema as Record<string, unknown>)
const variant = computed(() => {
  const format = schema.value.format
  if (format === 'password' || schema.value['x-secret'] === true) {
    return 'secret'
  }
  if (format === 'textarea' || schema.value['x-multiline'] === true || Number(schema.value.maxLength ?? 0) > 200) {
    return 'multiline'
  }
  if (format === 'uri' || format === 'url') {
    return 'url'
  }
  return 'text'
})
const revealed = ref(false)
const maxLength = computed(() => schema.value.maxLength as number | undefined)
const example = computed(() => {
  const examples = schema.value.examples
  return Array.isArray(examples) && examples.length ? String(examples[0]) : undefined
})

const update = (value: unknown) =>
  handleChange(control.value.path, value === '' || value === undefined ? undefined : String(value))
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ id, readonly, placeholder }">
      <UTextarea
        v-if="variant === 'multiline'"
        :id="id"
        :model-value="(control.data as string) ?? ''"
        :disabled="readonly"
        :placeholder="placeholder ?? example"
        :maxlength="maxLength"
        autoresize
        :rows="3"
        class="w-full"
        @update:model-value="update"
      />
      <UInput
        v-else
        :id="id"
        :model-value="(control.data as string) ?? ''"
        :type="variant === 'secret' && !revealed ? 'password' : variant === 'url' ? 'url' : 'text'"
        :disabled="readonly"
        :placeholder="placeholder ?? example"
        :maxlength="maxLength"
        :autocomplete="variant === 'secret' ? 'off' : undefined"
        class="w-full"
        :ui="{ base: variant === 'secret' || variant === 'url' ? 'font-mono' : '' }"
        @update:model-value="update"
      >
        <template v-if="variant === 'secret' || (variant === 'url' && control.data)" #trailing>
          <UButton
            v-if="variant === 'secret'"
            :icon="revealed ? 'i-lucide-eye-off' : 'i-lucide-eye'"
            :aria-label="revealed ? 'Hide' : 'Reveal'"
            color="neutral"
            variant="link"
            size="xs"
            @click="revealed = !revealed"
          />
          <UButton
            v-else
            :to="control.data as string"
            target="_blank"
            icon="i-lucide-external-link"
            aria-label="Open the link"
            color="neutral"
            variant="link"
            size="xs"
          />
        </template>
      </UInput>
      <p v-if="schema.pattern" class="text-xs text-dimmed">
        Pattern: <code class="font-mono">{{ schema.pattern }}</code>
      </p>
    </template>
  </ControlField>
</template>
