<script setup lang="ts">
import { computed } from 'vue'
import type { AccessAuditEntry } from '@42for.net/2splatform.sdk'
import { isLongAuditValue, isRoleValue, readableAuditValue } from '@/utils/access-audit'

/**
 * The change of an access audit record (424-263): the value before and after, a role as its badge and any other value
 * cut to one line, so a long description or compact JSON never widens the table. A long value, JSON or a long detail
 * (which the action column cuts to two lines) opens in full, indented, from the button at the end.
 */
const props = defineProps<{ entry: AccessAuditEntry }>()

const before = computed(() => props.entry.OldValue || undefined)
const after = computed(() => props.entry.NewValue || undefined)
const detail = computed(() => props.entry.Detail || undefined)
const expandable = computed(
  () => isLongAuditValue(before.value) || isLongAuditValue(after.value) || (detail.value?.length ?? 0) > 60,
)
</script>

<template>
  <div v-if="before || after || expandable" class="flex items-center gap-1.5">
    <template v-if="before">
      <RoleBadge v-if="isRoleValue(before)" :role="before" />
      <code v-else class="max-w-36 min-w-0 truncate font-mono text-xs" :title="before">{{ before }}</code>
      <UIcon name="i-lucide-arrow-right" class="size-3.5 shrink-0 text-muted" aria-hidden="true" />
      <span class="sr-only">to</span>
      <template v-if="after">
        <RoleBadge v-if="isRoleValue(after)" :role="after" />
        <code v-else class="max-w-36 min-w-0 truncate font-mono text-xs" :title="after">{{ after }}</code>
      </template>
      <span v-else class="text-xs text-muted">none</span>
    </template>
    <template v-else-if="after">
      <RoleBadge v-if="isRoleValue(after)" :role="after" />
      <code v-else class="max-w-72 min-w-0 truncate font-mono text-xs" :title="after">{{ after }}</code>
    </template>
    <UPopover v-if="expandable" :content="{ align: 'end' }">
      <UButton
        icon="i-lucide-maximize-2"
        aria-label="Show the full values"
        color="neutral"
        variant="ghost"
        size="xs"
        class="shrink-0"
      />
      <template #content>
        <!-- ph-no-capture: the popover leaves the table; its values can name people, as the table does -->
        <dl class="ph-no-capture max-h-[60vh] w-[min(36rem,90vw)] space-y-3 overflow-auto p-3 text-xs">
          <div v-if="before">
            <dt class="mb-1 font-semibold text-muted">Before</dt>
            <dd>
              <pre class="font-mono whitespace-pre-wrap wrap-anywhere">{{ readableAuditValue(before) }}</pre>
            </dd>
          </div>
          <div v-if="after">
            <dt class="mb-1 font-semibold text-muted">After</dt>
            <dd>
              <pre class="font-mono whitespace-pre-wrap wrap-anywhere">{{ readableAuditValue(after) }}</pre>
            </dd>
          </div>
          <div v-if="detail">
            <dt class="mb-1 font-semibold text-muted">Detail</dt>
            <dd class="whitespace-pre-wrap wrap-anywhere">{{ detail }}</dd>
          </div>
        </dl>
      </template>
    </UPopover>
  </div>
</template>
