<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { importKeyValues, restoreKeyValues, type KeyValueImportResult, type KeyValueProtection } from '@42for.net/2splatform.sdk'
import { encryptForRecipients, getHyokPlace } from '@42for.net/2splatform.sdk/hyok'
import { useKeyValuePolicy, useRecipients } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import {
  formatLabels,
  isClientEncrypted,
  isSecretLevel,
  levelMeta,
  levels,
  markMatching,
  parseImportText,
  toDocument,
  type ImportItem,
  type ParsedImport,
} from '@/utils/key-values'

/**
 * Import in three steps (spec 29.5): the source (a file or pasted text, format detected), the review (a level per
 * row, "mark matching", Merge or Replace, Check for the server's plan) and the result (Retry failed, Undo deletes).
 * HYOK rows are encrypted in the browser before they leave.
 */
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ imported: [] }>()

const context = useContextStore()
const toast = useToast()
const canSecrets = useCan('secrets')
const policy = useKeyValuePolicy()
const recipients = useRecipients()

const step = ref<1 | 2 | 3>(1)
const text = ref('')
const parsed = ref<ParsedImport>()
const parseError = ref<string>()
const items = ref<ImportItem[]>([])
const glob = ref('*password*')
const globLevel = ref<KeyValueProtection>('Standard')
const mode = ref<'Merge' | 'Replace'>('Merge')
const prefix = ref('')
const plan = ref<KeyValueImportResult>()
const result = ref<KeyValueImportResult>()
const busy = ref(false)
const failure = ref<string>()
const confirmReplace = ref(false)
const undone = ref(false)

watch(open, (isOpen) => {
  if (isOpen) {
    step.value = 1
    text.value = ''
    parsed.value = undefined
    parseError.value = undefined
    items.value = []
    plan.value = undefined
    result.value = undefined
    failure.value = undefined
    undone.value = false
    mode.value = 'Merge'
    prefix.value = ''
  }
})

const allowedLevels = computed(() => (policy.data.value?.AllowedProtections ?? ['Public', 'Standard']) as KeyValueProtection[])
const levelItems = computed(() =>
  levels
    .filter((level) => allowedLevels.value.includes(level) && (level === 'Public' || canSecrets.value))
    .map((level) => ({ label: levelMeta[level].label, value: level })),
)
const secretLevelItems = computed(() => levelItems.value.filter((item) => isSecretLevel(item.value)))

watch(
  () => policy.data.value?.DefaultSecretProtection,
  (value) => value && (globLevel.value = value as KeyValueProtection),
  { immediate: true },
)

async function readFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (file) {
    text.value = await file.text()
  }
}

function parse() {
  parseError.value = undefined
  try {
    parsed.value = parseImportText(text.value)
    items.value = parsed.value.items
    plan.value = undefined
    step.value = 2
  } catch (error) {
    parseError.value = error instanceof Error ? error.message : String(error)
  }
}

function mark() {
  if (glob.value.trim()) {
    items.value = markMatching(items.value, glob.value.trim(), globLevel.value)
  }
}

const invalid = computed(() => items.value.filter((item) => item.invalid).length)
const deletes = computed(() => plan.value?.Items?.filter((item) => item.Action === 'Delete').length ?? 0)

async function prepared(): Promise<ImportItem[]> {
  const enabled = (recipients.data.value ?? []).filter((recipient) => recipient.IsEnabled !== false)
  const output: ImportItem[] = []
  for (const item of items.value) {
    if (isClientEncrypted(item.protection) && context.scope) {
      if (enabled.length === 0) {
        throw new Error('The project has no enabled recipient for HYOK values.')
      }
      const { org, project, view } = context.scope
      output.push({ ...item, value: await encryptForRecipients(item.value, enabled, getHyokPlace(org, project, view, item.key), item.contentType ?? 'text/plain') })
    } else {
      output.push(item)
    }
  }
  return output
}

async function check() {
  if (!context.scope) {
    return
  }
  busy.value = true
  failure.value = undefined
  try {
    const { org, project, view } = context.scope
    plan.value = await importKeyValues(org, project, view, toDocument(await prepared()), {
      mode: mode.value,
      dryRun: true,
      keyPrefix: mode.value === 'Replace' && prefix.value ? prefix.value : undefined,
    })
  } catch (error) {
    failure.value = errorMessage(error)
  } finally {
    busy.value = false
  }
}

function apply() {
  if (mode.value === 'Replace' && deletes.value > 0) {
    confirmReplace.value = true
    return
  }
  void run(items.value)
}

async function run(subset: ImportItem[]) {
  if (!context.scope) {
    return
  }
  confirmReplace.value = false
  busy.value = true
  failure.value = undefined
  try {
    const { org, project, view } = context.scope
    const document = toDocument(await prepared().then((all) => all.filter((item) => subset.some((wanted) => wanted.key === item.key))))
    result.value = await importKeyValues(org, project, view, document, {
      mode: mode.value,
      keyPrefix: mode.value === 'Replace' && prefix.value ? prefix.value : undefined,
    })
    step.value = 3
    emit('imported')
  } catch (error) {
    failure.value = errorMessage(error)
  } finally {
    busy.value = false
  }
}

const failed = computed(() => (result.value?.Items ?? []).filter((item) => item.ErrorCode))
const deletedKeys = computed(() => (result.value?.Items ?? []).filter((item) => item.Action === 'Delete').map((item) => item.Key ?? ''))

function retryFailed() {
  const keys = new Set(failed.value.map((item) => item.Key))
  mode.value = 'Merge'
  void run(items.value.filter((item) => keys.has(item.key)))
}

async function undoDeletes() {
  if (!context.scope || deletedKeys.value.length === 0) {
    return
  }
  busy.value = true
  try {
    const { org, project, view } = context.scope
    await restoreKeyValues(org, project, view, { Keys: deletedKeys.value })
    undone.value = true
    emit('imported')
    toast.add({ title: `${deletedKeys.value.length} key(s) restored`, color: 'success', icon: 'i-lucide-undo-2' })
  } catch (error) {
    toast.add({ title: 'The keys were not restored', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

const counts = (entries: { Action?: string }[] | undefined) => {
  const map = new Map<string, number>()
  for (const entry of entries ?? []) {
    map.set(entry.Action ?? '?', (map.get(entry.Action ?? '?') ?? 0) + 1)
  }
  return [...map].map(([action, count]) => `${count} ${action.toLowerCase()}`).join(', ')
}
</script>

<template>
  <UModal v-model:open="open" class="ph-no-capture" title="Import key values" :description="`Into ${context.org}/${context.project}, view ${context.view}.`" :ui="{ content: 'max-w-3xl' }">
    <template #body>
      <div class="space-y-4">
        <ol class="flex gap-4 text-xs text-muted" aria-label="Steps">
          <li :class="step === 1 ? 'font-semibold text-highlighted' : ''">1. Source</li>
          <li :class="step === 2 ? 'font-semibold text-highlighted' : ''">2. Review</li>
          <li :class="step === 3 ? 'font-semibold text-highlighted' : ''">3. Result</li>
        </ol>

        <!-- 1 -->
        <template v-if="step === 1">
          <UFormField label="File" help="A key/value document (JSON), a .env file or a JSON object.">
            <input type="file" accept=".json,.env,text/plain,application/json" class="text-sm" @change="readFile" />
          </UFormField>
          <UFormField label="Or paste the text" :error="parseError">
            <UTextarea v-model="text" class="w-full font-mono" :rows="8" placeholder="DB__HOST=sql.internal&#10;DB__PASSWORD=…" />
          </UFormField>
        </template>

        <!-- 2 -->
        <template v-else-if="step === 2">
          <div class="flex flex-wrap items-center gap-2 text-sm">
            <UBadge :label="formatLabels[parsed?.format ?? 'unknown']" color="neutral" variant="subtle" />
            <span class="text-muted">{{ items.length }} row(s)</span>
            <span v-if="invalid" class="text-error">{{ invalid }} invalid key(s)</span>
          </div>

          <div v-if="parsed?.format !== 'document' && secretLevelItems.length" class="flex flex-wrap items-end gap-2">
            <UFormField label="Mark keys matching">
              <UInput v-model="glob" class="w-48 font-mono" placeholder="*password*" />
            </UFormField>
            <UFormField label="as">
              <USelect v-model="globLevel" :items="secretLevelItems" class="w-36" />
            </UFormField>
            <UButton label="Mark" color="neutral" variant="outline" @click="mark" />
          </div>

          <div class="max-h-72 overflow-auto rounded-md border border-default">
            <table class="w-full text-sm">
              <thead class="bg-elevated/50 text-xs text-muted">
                <tr>
                  <th class="px-3 py-2 text-start font-medium">Key</th>
                  <th class="px-3 py-2 text-start font-medium">Level</th>
                  <th class="px-3 py-2 text-start font-medium">Value</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                <tr v-for="(item, index) in items" :key="item.key + index" :class="item.invalid ? 'bg-error/5' : ''">
                  <td class="px-3 py-1.5 font-mono text-xs" :class="item.invalid ? 'text-error' : ''">{{ item.key }}</td>
                  <td class="px-3 py-1.5">
                    <USelect
                      :model-value="item.protection"
                      :items="levelItems"
                      size="xs"
                      class="w-32"
                      :aria-label="`Level of ${item.key}`"
                      @update:model-value="(value) => (items[index] = { ...item, protection: value as KeyValueProtection })"
                    />
                  </td>
                  <td class="max-w-xs truncate px-3 py-1.5 font-mono text-xs text-muted">
                    {{ isSecretLevel(item.protection) ? '••••••••' : item.value }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="flex flex-wrap items-end gap-2">
            <UFormField label="Mode">
              <USelect
                v-model="mode"
                :items="[
                  { label: 'Merge: write the listed keys', value: 'Merge' },
                  { label: 'Replace: also delete the other keys', value: 'Replace' },
                ]"
                class="w-72"
              />
            </UFormField>
            <UFormField v-if="mode === 'Replace'" label="Only under prefix">
              <UInput v-model="prefix" class="w-40 font-mono" placeholder="db." />
            </UFormField>
            <UButton label="Check" icon="i-lucide-list-checks" color="neutral" variant="outline" :loading="busy" :disabled="invalid > 0" @click="check" />
          </div>

          <div v-if="plan" class="space-y-2 rounded-md border border-default p-3 text-sm">
            <p class="font-medium">Plan: {{ counts(plan.Items) }}</p>
            <ul class="max-h-40 overflow-auto space-y-0.5 text-xs">
              <li v-for="entry in plan.Items" :key="entry.Key" class="flex gap-2 font-mono">
                <span class="w-20 text-muted">{{ entry.Action }}</span>
                <span>{{ entry.Key }}</span>
                <span v-if="entry.Message" class="text-error">{{ entry.Message }}</span>
              </li>
            </ul>
          </div>
          <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />
        </template>

        <!-- 3 -->
        <template v-else>
          <UAlert
            :color="result?.IsComplete ? 'success' : 'warning'"
            variant="subtle"
            :icon="result?.IsComplete ? 'i-lucide-check' : 'i-lucide-triangle-alert'"
            :title="result?.IsComplete ? 'Imported' : 'Imported with failures'"
            :description="counts(result?.Items)"
          />
          <ul class="max-h-60 overflow-auto space-y-0.5 text-xs">
            <li v-for="entry in result?.Items" :key="entry.Key" class="flex gap-2 font-mono">
              <span class="w-20 text-muted">{{ entry.Action }}</span>
              <span>{{ entry.Key }}</span>
              <span v-if="entry.Version" class="text-muted">v{{ entry.Version }}</span>
              <span v-if="entry.Message" class="text-error">{{ entry.Message }}</span>
            </li>
          </ul>
          <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />
        </template>
      </div>
    </template>
    <template #footer>
      <div class="flex w-full flex-wrap justify-end gap-2">
        <template v-if="step === 1">
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
          <UButton label="Next" icon="i-lucide-arrow-right" trailing :disabled="!text.trim()" @click="parse" />
        </template>
        <template v-else-if="step === 2">
          <UButton label="Back" color="neutral" variant="ghost" @click="step = 1" />
          <UButton label="Import" icon="i-lucide-upload" :loading="busy" :disabled="!plan || invalid > 0" @click="apply" />
        </template>
        <template v-else>
          <UButton v-if="failed.length" label="Retry failed" icon="i-lucide-rotate-ccw" color="neutral" variant="outline" :loading="busy" @click="retryFailed" />
          <UButton v-if="deletedKeys.length && !undone" label="Undo deletes" icon="i-lucide-undo-2" color="warning" variant="outline" :loading="busy" @click="undoDeletes" />
          <UButton label="Done" @click="open = false" />
        </template>
      </div>
    </template>
  </UModal>

  <ConfirmDialog
    v-model:open="confirmReplace"
    title="Replace deletes keys"
    :description="`${deletes} key(s) of the view that the document does not list will be deleted (restorable for the retention period).`"
    :confirm-text="context.view"
    confirm-label="Replace"
    :loading="busy"
    @confirm="run(items)"
  />
</template>
