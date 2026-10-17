<script setup lang="ts">
import { computed, ref } from 'vue'
import { useKeyValueUsage } from '@/queries/scope'

// The key/value usage of the project (spec 29.7), the Usage tab of Monitor (424-35): the totals of the range, the
// estimated charges, and a daily chart.
const canAdmin = useCan('manageAccess')

const today = new Date()
const iso = (date: Date) => date.toISOString().slice(0, 10)
const from = ref(iso(new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1))))
const to = ref(iso(today))

const params = computed(() => ({ from: from.value || undefined, to: to.value || undefined }))
const report = useKeyValueUsage(params, canAdmin)

const meterLabels: Record<string, string> = {
  'keys.public': 'Public keys (key-months)',
  'keys.reference': 'References (key-months)',
  'keys.standard': 'Standard keys (key-months)',
  'keys.hsm': 'HSM keys (key-months)',
  'keys.byok': 'BYOK keys (key-months)',
  'keys.hyok': 'HYOK keys (key-months)',
  versions: 'Stored versions (average)',
  'storage.bytes': 'Stored bytes (average)',
  'reads.api': 'API reads',
  'reads.binding': 'Binding reads',
  reveals: 'Secret values delivered',
  writes: 'Versions written',
  'kms.standard': 'Standard key operations',
  'kms.hsm': 'HSM key operations',
  'kms.byok': 'Customer key operations',
}

const totals = computed(() =>
  Object.entries(report.data.value?.Totals ?? {})
    .filter(([, value]) => value !== 0)
    .toSorted(([a], [b]) => a.localeCompare(b))
    .map(([meter, value]) => ({ meter, label: meterLabels[meter] ?? meter, value })),
)
const charges = computed(() =>
  Object.entries(report.data.value?.EstimatedCharges ?? {}).filter(([, value]) => value > 0),
)
const chargesTotal = computed(() => charges.value.reduce((sum, [, value]) => sum + value, 0))

const days = computed(() => report.data.value?.Days ?? [])
const reads = (values: Record<string, number> | undefined) =>
  (values?.['reads.api'] ?? 0) + (values?.['reads.binding'] ?? 0)
const keys = (values: Record<string, number> | undefined) =>
  ['keys.public', 'keys.reference', 'keys.standard', 'keys.hsm', 'keys.byok', 'keys.hyok'].reduce(
    (sum, meter) => sum + (values?.[meter] ?? 0),
    0,
  )
const maxReads = computed(() => Math.max(1, ...days.value.map((entry) => reads(entry.Values))))
const maxKeys = computed(() => Math.max(1, ...days.value.map((entry) => keys(entry.Values))))

const format = (value: number) => new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value)
const money = (value: number) => new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD' }).format(value)
</script>

<template>
  <div class="mx-auto w-full max-w-4xl space-y-6">
    <div class="flex flex-wrap items-end gap-3">
      <UFormField label="From">
        <UInput v-model="from" type="date" class="w-40" aria-label="From" />
      </UFormField>
      <UFormField label="To">
        <UInput v-model="to" type="date" class="w-40" aria-label="To" />
      </UFormField>
    </div>
    <LoadingState v-if="report.isPending.value" />
    <ErrorState v-else-if="report.isError.value" :error="report.error.value" compact @retry="report.refetch()" />
    <template v-else-if="report.data.value">
      <div class="grid gap-4 lg:grid-cols-2">
        <UCard>
          <template #header
            ><h2 class="font-semibold">Totals {{ report.data.value.From }} to {{ report.data.value.To }}</h2></template
          >
          <p v-if="totals.length === 0" class="text-sm text-muted">No usage recorded in this range.</p>
          <dl v-else class="grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-1 text-sm">
            <template v-for="entry in totals" :key="entry.meter">
              <dt class="text-muted">{{ entry.label }}</dt>
              <dd class="text-end font-mono">{{ format(entry.value) }}</dd>
            </template>
          </dl>
        </UCard>
        <UCard>
          <template #header><h2 class="font-semibold">Estimated charges</h2></template>
          <p v-if="charges.length === 0" class="text-sm text-muted">
            The deployment has no prices configured; the meters are informational.
          </p>
          <template v-else>
            <dl class="grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-1 text-sm">
              <template v-for="[item, value] in charges" :key="item">
                <dt class="text-muted">{{ meterLabels[item] ?? item }}</dt>
                <dd class="text-end font-mono">{{ money(value) }}</dd>
              </template>
              <dt class="font-medium">Total</dt>
              <dd class="text-end font-mono font-medium">{{ money(chargesTotal) }}</dd>
            </dl>
            <p class="mt-2 text-xs text-muted">An estimate from the list prices; the invoice is authoritative.</p>
          </template>
        </UCard>
      </div>

      <UCard>
        <template #header><h2 class="font-semibold">Per day</h2></template>
        <p v-if="days.length === 0" class="text-sm text-muted">No days with data.</p>
        <div v-else class="space-y-3">
          <div class="flex gap-4 text-xs text-muted">
            <span class="inline-flex items-center gap-1"
              ><span class="inline-block size-2 rounded-sm bg-primary" /> reads (API and bindings)</span
            >
            <span class="inline-flex items-center gap-1"
              ><span class="inline-block size-2 rounded-sm bg-warning" /> keys</span
            >
          </div>
          <div
            class="flex h-40 items-end gap-px overflow-x-auto"
            role="img"
            :aria-label="`Daily reads and keys for ${days.length} days`"
          >
            <div
              v-for="entry in days"
              :key="entry.Date"
              class="flex min-w-2 flex-1 flex-col justify-end gap-px"
              :title="`${entry.Date}: ${format(reads(entry.Values))} reads, ${format(keys(entry.Values))} keys`"
            >
              <span
                class="block w-full rounded-t-sm bg-primary"
                :style="{ height: `${(reads(entry.Values) / maxReads) * 70}%` }"
              />
              <span class="block w-full bg-warning" :style="{ height: `${(keys(entry.Values) / maxKeys) * 25}%` }" />
            </div>
          </div>
          <div class="flex justify-between text-xs text-muted">
            <span>{{ days[0]?.Date }}</span>
            <span>{{ days.at(-1)?.Date }}</span>
          </div>
        </div>
      </UCard>
    </template>
  </div>
</template>
