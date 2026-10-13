<script setup lang="ts">
import { computed } from 'vue'
import { builtInTypeMeta, typeCodes, type AnnotationTypeCode } from '@/utils/annotation-key'
import {
  maxTypeNameLength,
  pluralize,
  sanitizeTypeAppearances,
  typeColors,
  typeIcons,
  type ResolvedTypeAppearance,
  type TypeAppearance,
  type TypeAppearances,
  type TypeColor,
} from '@/utils/type-appearance'

/**
 * Names, colors and icons of the seven annotation types for one layer (project settings or Preferences, 424-13).
 * An empty field falls through to `inherited`. Every change emits the sanitized layer; names update on change rather
 * than on every key, so they can contain spaces. The fields are labelled by the built-in type name, which never changes.
 */
const model = defineModel<TypeAppearances>({ required: true })
const props = withDefaults(
  defineProps<{
    /** What applies to an empty field. */
    inherited: Record<AnnotationTypeCode, ResolvedTypeAppearance>
    disabled?: boolean
  }>(),
  { disabled: false },
)

// Select items cannot have an empty value.
const inherit = 'default'

const swatch = (color: TypeColor) => ({
  '--swatch-light': typeColors[color].light,
  '--swatch-dark': typeColors[color].dark,
})

const rows = computed(() =>
  typeCodes.map((code) => {
    const own: TypeAppearance = model.value[code] ?? {}
    const inherited = props.inherited[code]
    const label = own.label ?? inherited.label
    return {
      code,
      name: builtInTypeMeta[code].label,
      own,
      inherited,
      preview: { label, color: own.color ?? inherited.color, icon: own.icon ?? inherited.icon },
      pluralPlaceholder: own.label ? pluralize(own.label) : inherited.plural,
      changed: Object.keys(own).length > 0,
      colors: [
        // "Default" keeps the trigger short; the list names the inherited color.
        { label: 'Default', value: inherit, color: inherited.color, hint: typeColors[inherited.color].label },
        ...Object.entries(typeColors).map(([value, color]) => ({
          label: color.label,
          value,
          color: value as TypeColor,
        })),
      ],
      icons: [
        { label: 'Default', value: inherit, icon: inherited.icon },
        ...typeIcons.map((icon) => ({ label: icon.label, value: icon.name, icon: icon.name })),
      ],
    }
  }),
)

function patch(code: AnnotationTypeCode, change: TypeAppearance) {
  model.value = sanitizeTypeAppearances({ ...model.value, [code]: { ...model.value[code], ...change } })
}

function resetType(code: AnnotationTypeCode) {
  const { [code]: _removed, ...others } = model.value
  model.value = others
}

const fromSelect = <T extends string>(value: unknown): T | undefined =>
  typeof value === 'string' && value !== inherit ? (value as T) : undefined
</script>

<template>
  <ul class="divide-y divide-default">
    <li v-for="row in rows" :key="row.code" class="space-y-3 py-4 first:pt-0 last:pb-0">
      <div class="flex min-h-8 items-center gap-3">
        <span
          class="inline-flex items-center gap-1.5 text-sm font-medium text-(--swatch-light) dark:text-(--swatch-dark)"
          :style="swatch(row.preview.color)"
          :data-testid="`type-preview-${row.code}`"
        >
          <UIcon :name="row.preview.icon" class="size-4 shrink-0" />
          {{ row.preview.label }}
        </span>
        <span class="font-mono text-xs text-dimmed">{{ row.code }}</span>
        <UButton
          v-if="row.changed && !disabled"
          class="ms-auto"
          icon="i-lucide-rotate-ccw"
          color="neutral"
          variant="ghost"
          size="xs"
          label="Reset"
          :aria-label="`Reset ${row.name}`"
          @click="resetType(row.code)"
        />
      </div>

      <div class="grid grid-cols-2 gap-3 lg:grid-cols-4" role="group" :aria-label="row.name">
        <UFormField label="Name">
          <UInput
            :model-value="row.own.label ?? ''"
            :model-modifiers="{ lazy: true }"
            :placeholder="row.inherited.label"
            :maxlength="maxTypeNameLength"
            :disabled="disabled"
            :aria-label="`${row.name} name`"
            class="w-full"
            @update:model-value="(value) => patch(row.code, { label: String(value ?? '') })"
          />
        </UFormField>
        <UFormField label="Plural">
          <UInput
            :model-value="row.own.plural ?? ''"
            :model-modifiers="{ lazy: true }"
            :placeholder="row.pluralPlaceholder"
            :maxlength="maxTypeNameLength"
            :disabled="disabled || !row.own.label"
            :title="row.own.label ? undefined : 'Set a name first'"
            :aria-label="`${row.name} plural`"
            class="w-full"
            @update:model-value="(value) => patch(row.code, { plural: String(value ?? '') })"
          />
        </UFormField>
        <UFormField label="Color">
          <USelect
            :model-value="row.own.color ?? inherit"
            :items="row.colors"
            :disabled="disabled"
            :aria-label="`${row.name} color`"
            class="w-full"
            @update:model-value="(value) => patch(row.code, { color: fromSelect<TypeColor>(value) })"
          >
            <template #leading>
              <span
                class="size-3 rounded-full bg-(--swatch-light) dark:bg-(--swatch-dark)"
                :style="swatch(row.preview.color)"
                aria-hidden="true"
              />
            </template>
            <template #item-leading="{ item }">
              <span class="flex size-5 shrink-0 items-center justify-center" aria-hidden="true">
                <span
                  class="size-3 rounded-full bg-(--swatch-light) dark:bg-(--swatch-dark)"
                  :style="swatch((item as { color: TypeColor }).color)"
                />
              </span>
            </template>
            <template #item-trailing="{ item }">
              <span v-if="(item as { hint?: string }).hint" class="text-xs text-dimmed">
                {{ (item as { hint?: string }).hint }}
              </span>
            </template>
          </USelect>
        </UFormField>
        <UFormField label="Icon">
          <USelectMenu
            :model-value="row.own.icon ?? inherit"
            :items="row.icons"
            value-key="value"
            :icon="row.preview.icon"
            :disabled="disabled"
            :search-input="{ placeholder: 'Search icons…' }"
            :aria-label="`${row.name} icon`"
            class="w-full"
            @update:model-value="(value: unknown) => patch(row.code, { icon: fromSelect<string>(value) })"
          />
        </UFormField>
      </div>
    </li>
  </ul>
</template>
