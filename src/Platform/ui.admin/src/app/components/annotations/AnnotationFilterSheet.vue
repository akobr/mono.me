<script setup lang="ts">
import { computed, ref } from 'vue'
import { annotationStatuses, statusMeta } from '@/utils/annotation-status'
import { typeCodes, typeMeta } from '@/utils/annotation-key'
import { parseFilter, removeTerm, setQualifier, type Qualifier } from '@/utils/filter-parser'

/**
 * The filters of the annotation screens as a bottom sheet below md (spec section 15): type, status and
 * configuration toggles that write the same filter language as the box, the active terms, and examples.
 */
const props = defineProps<{ query: string }>()
const emit = defineEmits<{ 'update:query': [value: string] }>()

const open = ref(false)
// Parsed here from the box's text, so quick toggles never work on a stale (debounced) filter.
const parsed = computed(() => parseFilter(props.query))

const valuesOf = (qualifier: Qualifier) =>
  parsed.value.terms.find((term) => term.qualifier === qualifier && !term.negated)?.values ?? []

function toggle(qualifier: Qualifier, value: string) {
  const current = valuesOf(qualifier)
  const next = current.includes(value) ? current.filter((item) => item !== value) : [...current, value]
  emit('update:query', setQualifier(props.query, qualifier, next))
}

const types = computed(() =>
  typeCodes.map((code) => ({ code, meta: typeMeta[code], on: valuesOf('type').includes(code) })),
)
const statuses = computed(() =>
  annotationStatuses.map((status) => ({ status, meta: statusMeta[status], on: valuesOf('is').includes(status) })),
)
const configured = computed(() => valuesOf('has').includes('config'))

const examples = [
  ['label:eu,us -is:disabled', 'labelled eu or us, not disabled'],
  ['subject:northwind has:config', 'northwind with a configuration'],
  ['ancestor:rst.invoicing', 'everything below rst.invoicing'],
] as const
</script>

<template>
  <UDrawer v-model:open="open" title="Filters" description="Narrow the annotations of this view.">
    <UButton
      icon="i-lucide-sliders-horizontal"
      color="neutral"
      variant="outline"
      :label="parsed.terms.length ? String(parsed.terms.length) : undefined"
      aria-label="Filters"
    />

    <template #body>
      <div class="space-y-5 pb-4">
        <section class="space-y-2">
          <h3 class="text-sm font-semibold text-highlighted">Type</h3>
          <div class="flex flex-wrap gap-2">
            <UButton
              v-for="type in types"
              :key="type.code"
              :icon="type.meta.icon"
              :label="type.meta.plural"
              :color="type.on ? 'primary' : 'neutral'"
              :variant="type.on ? 'soft' : 'outline'"
              :aria-pressed="type.on"
              class="min-h-10"
              @click="toggle('type', type.code)"
            />
          </div>
        </section>

        <section class="space-y-2">
          <h3 class="text-sm font-semibold text-highlighted">Status</h3>
          <div class="flex flex-wrap gap-2">
            <UButton
              v-for="item in statuses"
              :key="item.status"
              :icon="item.meta.icon"
              :label="item.meta.label"
              :color="item.on ? 'primary' : 'neutral'"
              :variant="item.on ? 'soft' : 'outline'"
              :aria-pressed="item.on"
              class="min-h-10"
              @click="toggle('is', item.status)"
            />
          </div>
        </section>

        <section class="space-y-2">
          <h3 class="text-sm font-semibold text-highlighted">Configuration</h3>
          <UButton
            icon="i-lucide-settings-2"
            label="Has a configuration"
            :color="configured ? 'primary' : 'neutral'"
            :variant="configured ? 'soft' : 'outline'"
            :aria-pressed="configured"
            class="min-h-10"
            @click="toggle('has', 'config')"
          />
        </section>

        <section v-if="parsed.terms.length" class="space-y-2">
          <div class="flex items-center justify-between">
            <h3 class="text-sm font-semibold text-highlighted">Active filter</h3>
            <UButton label="Clear all" color="neutral" variant="link" size="sm" @click="emit('update:query', '')" />
          </div>
          <!-- ph-no-capture: the labels are the filter text. -->
          <div class="ph-no-capture flex flex-wrap gap-2">
            <UButton
              v-for="(term, index) in parsed.terms"
              :key="`${index}:${term.raw}`"
              :label="term.raw"
              trailing-icon="i-lucide-x"
              :color="term.error ? 'warning' : 'neutral'"
              variant="soft"
              class="min-h-10 font-mono"
              :aria-label="`Remove ${term.raw}`"
              @click="emit('update:query', removeTerm(parsed, index))"
            />
          </div>
        </section>

        <section class="space-y-2">
          <h3 class="text-sm font-semibold text-highlighted">Examples</h3>
          <ul class="space-y-1">
            <li v-for="[example, description] in examples" :key="example">
              <button
                type="button"
                class="flex min-h-10 w-full flex-col items-start rounded-md px-2 py-1.5 text-start hover:bg-elevated"
                @click="emit('update:query', example)"
              >
                <span class="font-mono text-sm text-primary">{{ example }}</span>
                <span class="text-xs text-muted">{{ description }}</span>
              </button>
            </li>
          </ul>
        </section>
      </div>
    </template>

    <template #footer>
      <UButton label="Done" block size="lg" @click="open = false" />
    </template>
  </UDrawer>
</template>
