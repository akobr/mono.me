<script setup lang="ts">
import { computed } from 'vue'
import type { ViewInboxItem } from '@42for.net/2splatform.sdk'
import { inboxAppearance, inboxClosedLabel, inboxOffers, isAdministratorsOnly } from '@/utils/inbox'

// One item of the inbox of a view (424-31, docs/Platform/Storyteller/inbox.md): the icon of its kind and level, its
// title and text as the producer wrote them, when it arrived and who caused it, of a closed item who closed it, and
// the buttons of what it offers. The page runs them: the row only says which one was pressed. On a phone the buttons
// go under the text.
const props = defineProps<{
  item: ViewInboxItem
  /** An accept or a dismiss of the item is on its way. */
  busy?: boolean
  /** The archive of the item's export is being fetched. */
  downloading?: boolean
}>()
defineEmits<{ accept: []; dismiss: []; download: []; result: [] }>()

const appearance = computed(() => inboxAppearance(props.item))
const offers = computed(() => inboxOffers(props.item))
const closed = computed(() => inboxClosedLabel(props.item))
const hasButtons = computed(() => Object.values(offers.value).some(Boolean))
</script>

<template>
  <div class="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-start">
    <div class="flex min-w-0 flex-1 items-start gap-3">
      <span class="mt-0.5 flex shrink-0" :title="appearance.label">
        <UIcon :name="appearance.icon" class="size-5" :class="appearance.iconClass" aria-hidden="true" />
      </span>
      <div class="min-w-0 flex-1 space-y-1">
        <p class="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span class="sr-only">{{ appearance.label }}:</span>
          <span class="font-medium wrap-anywhere text-highlighted">{{ item.Title }}</span>
          <UBadge
            v-if="isAdministratorsOnly(item)"
            label="Administrators only"
            icon="i-lucide-shield"
            color="neutral"
            variant="subtle"
            size="sm"
          />
        </p>
        <!-- the error of a job is one long line with tokens that have no space to break at -->
        <p v-if="item.Text" class="text-sm wrap-anywhere text-muted">{{ item.Text }}</p>
        <!-- one line of text each, so the words keep their spaces for a screen reader -->
        <p class="text-xs text-muted">
          <RelativeTime :value="item.CreatedAt" /> by <AuditActorLink :actor="item.CreatedBy" subtle />
          <template v-if="item.CreatedBy?.Via"> via {{ item.CreatedBy.Via }}</template>
        </p>
        <p v-if="closed" class="text-xs text-muted">
          {{ closed }} by <AuditActorLink :actor="item.Closed?.By" subtle />
          <template v-if="item.Closed?.By?.Via"> via {{ item.Closed.By.Via }}</template>,
          <RelativeTime :value="item.Closed?.At" />
        </p>
      </div>
    </div>
    <div v-if="hasButtons" class="flex shrink-0 flex-wrap items-center gap-2 ps-8 sm:justify-end sm:ps-0">
      <span v-if="offers.download && item.ExpiresAt" class="text-xs text-muted">
        Expires <RelativeTime :value="item.ExpiresAt" />
      </span>
      <UButton
        v-if="offers.download"
        label="Download"
        icon="i-lucide-download"
        color="neutral"
        variant="outline"
        size="sm"
        :loading="downloading"
        @click="$emit('download')"
      />
      <UButton
        v-if="offers.result"
        label="Show the result"
        icon="i-lucide-list-checks"
        color="neutral"
        variant="outline"
        size="sm"
        @click="$emit('result')"
      />
      <UButton
        v-if="offers.accept"
        label="Accept"
        icon="i-lucide-check"
        size="sm"
        :disabled="busy"
        @click="$emit('accept')"
      />
      <UButton
        v-if="offers.dismiss"
        label="Dismiss"
        icon="i-lucide-x"
        color="neutral"
        variant="ghost"
        size="sm"
        :disabled="busy"
        @click="$emit('dismiss')"
      />
    </div>
  </div>
</template>
