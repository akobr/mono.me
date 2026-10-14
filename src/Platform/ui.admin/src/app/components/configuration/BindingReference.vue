<script setup lang="ts">
import { computed, ref } from 'vue'
import type { BindingExample, BindingParameterDescription } from '@42for.net/2splatform.sdk'
import { injectBindingAssist } from '@/composables/useBindingAssist'

/**
 * The binding reference (424-10): a floating view with the expression forms, the functions and sources the server
 * registered, and the object bindings, each with examples to copy. With `insertable`, **Use** emits the example of a
 * string form, which the parent puts into the value.
 */
withDefaults(defineProps<{ insertable?: boolean; label?: string }>(), { insertable: false, label: 'Bindings' })
const emit = defineEmits<{ insert: [expression: string] }>()

interface Entry {
  id: string
  title: string
  name?: string
  summary?: string
  syntax?: string
  badges: string[]
  parameters: BindingParameterDescription[]
  examples: BindingExample[]
  /** An object binding: its examples are JSON objects, not strings to put into a value. */
  envelope?: boolean
}

interface Section {
  id: string
  title: string
  entries: Entry[]
  empty?: string
}

const assist = injectBindingAssist()
const open = ref(false)
const filter = ref('')

const sections = computed<Section[]>(() => {
  const catalog = assist.catalog.value
  const all: Section[] = [
    {
      id: 'expressions',
      title: 'Expressions',
      entries: catalog.expressions.map((entry) => ({
        id: entry.id,
        title: entry.title,
        summary: entry.summary,
        syntax: entry.syntax,
        badges: [],
        parameters: [],
        examples: entry.examples,
      })),
    },
    {
      id: 'functions',
      title: 'Functions',
      empty: 'No function is registered on this server.',
      entries: catalog.functions.map((entry) => ({
        id: `function-${entry.Name}`,
        title: 'Function',
        name: entry.Name,
        summary: entry.Summary,
        syntax: entry.Signature ? `@${entry.Signature}` : `@${entry.Name}(…)`,
        badges: [],
        parameters: entry.Parameters ?? [],
        examples: entry.Examples ?? [],
      })),
    },
    {
      id: 'sources',
      title: 'Sources',
      empty: catalog.known ? 'No source is registered on this server.' : 'Loading the sources of this server…',
      entries: catalog.sources.map((entry) => ({
        id: `source-${entry.Name}`,
        title: 'Source',
        name: entry.Name,
        summary: entry.Summary,
        syntax: entry.IsDefault ? '@path.to.value' : `@(path.to.value, ${entry.Name})`,
        badges: [entry.IsSecret ? 'secret' : '', entry.IsDefault ? 'default' : ''].filter(Boolean),
        parameters: [],
        examples: entry.Examples ?? [],
      })),
    },
    {
      id: 'objects',
      title: 'Object bindings',
      entries: catalog.envelopes.map((entry) => ({
        id: entry.id,
        title: entry.title,
        summary: entry.summary,
        syntax: entry.syntax,
        badges: [],
        parameters: [],
        examples: entry.examples.map((example) => ({
          ...example,
          Expression: JSON.stringify(JSON.parse(example.Expression ?? '{}'), null, 2),
        })),
        envelope: true,
      })),
    },
  ]

  const needle = filter.value.trim().toLowerCase()
  if (!needle) {
    return all
  }
  return all
    .map((section) => ({
      ...section,
      entries: section.entries.filter((entry) =>
        [entry.title, entry.name, entry.summary, entry.syntax, ...entry.examples.map((example) => example.Expression)]
          .filter((text): text is string => !!text)
          .some((text) => text.toLowerCase().includes(needle)),
      ),
    }))
    .filter((section) => section.entries.length > 0)
})

function use(expression: string | undefined) {
  if (expression) {
    emit('insert', expression)
    open.value = false
  }
}
</script>

<template>
  <UPopover v-model:open="open" :content="{ align: 'start' }">
    <slot>
      <UButton :label="label" size="xs" color="neutral" variant="ghost">
        <template #leading>
          <span class="font-mono text-xs font-semibold text-info italic">fx</span>
        </template>
      </UButton>
    </slot>

    <template #content>
      <div class="flex max-h-[70vh] w-[min(32rem,90vw)] flex-col" role="region" aria-label="Binding reference">
        <div class="border-b border-default p-2">
          <UInput
            v-model="filter"
            placeholder="Filter functions, sources and examples"
            icon="i-lucide-search"
            size="sm"
            class="w-full"
            aria-label="Filter the binding reference"
          />
        </div>

        <div class="min-h-0 flex-1 space-y-4 overflow-y-auto p-3 text-xs">
          <p v-if="sections.length === 0" class="text-dimmed">Nothing matches “{{ filter }}”.</p>
          <section v-for="section in sections" :key="section.id" :data-section="section.id" class="space-y-2">
            <h3 class="text-[11px] font-semibold tracking-wide text-muted uppercase">{{ section.title }}</h3>
            <p v-if="section.entries.length === 0" class="text-dimmed">{{ section.empty }}</p>

            <article
              v-for="entry in section.entries"
              :key="entry.id"
              :data-binding="entry.id"
              class="space-y-1.5 rounded-md border border-default p-2"
            >
              <div class="flex flex-wrap items-center gap-1.5">
                <span class="font-medium text-highlighted">{{ entry.title }}</span>
                <code v-if="entry.name" class="font-mono text-highlighted">{{ entry.name }}</code>
                <UBadge
                  v-for="badge in entry.badges"
                  :key="badge"
                  :label="badge"
                  :icon="badge === 'secret' ? 'i-lucide-lock' : undefined"
                  :color="badge === 'secret' ? 'warning' : 'neutral'"
                  variant="subtle"
                  size="sm"
                />
              </div>
              <MarkdownText v-if="entry.summary" :source="entry.summary" class="text-muted" />
              <code v-if="entry.syntax" class="block font-mono text-[11px] break-all text-muted">{{
                entry.syntax
              }}</code>

              <dl v-if="entry.parameters.length" class="space-y-1">
                <div v-for="parameter in entry.parameters" :key="parameter.Name" class="flex flex-wrap gap-x-1.5">
                  <dt>
                    <code class="font-mono">{{ parameter.Name }}</code>
                    <span v-if="parameter.IsOptional" class="text-dimmed"> (optional)</span>
                  </dt>
                  <dd class="min-w-0 flex-1 text-muted">
                    <MarkdownText :source="parameter.Description" />
                    <span v-if="parameter.Values?.length" class="text-dimmed">{{ parameter.Values.join(', ') }}</span>
                  </dd>
                </div>
              </dl>

              <div
                v-for="example in entry.examples"
                :key="example.Expression"
                class="flex items-start gap-1 rounded bg-elevated/60 px-2 py-1.5"
              >
                <div class="min-w-0 flex-1 space-y-0.5">
                  <pre v-if="entry.envelope" class="font-mono text-[11px] whitespace-pre-wrap">{{
                    example.Expression
                  }}</pre>
                  <code v-else class="block font-mono break-all text-highlighted">{{ example.Expression }}</code>
                  <MarkdownText v-if="example.Description" :source="example.Description" class="text-dimmed" />
                </div>
                <CopyButton :value="example.Expression ?? ''" />
                <UButton
                  v-if="insertable && !entry.envelope"
                  label="Use"
                  size="xs"
                  color="primary"
                  variant="soft"
                  @click="use(example.Expression)"
                />
              </div>
            </article>
          </section>

          <UButton
            to="https://42for.net/platform/configuration#bindings"
            target="_blank"
            label="Bindings in the documentation"
            trailing-icon="i-lucide-external-link"
            color="primary"
            variant="link"
            size="xs"
            class="px-0"
          />
        </div>
      </div>
    </template>
  </UPopover>
</template>
