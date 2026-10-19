<script setup lang="ts">
import type { DiffHunk } from '@42for.net/2splatform.sdk'

/** The server hunks as a read-only unified view, for versions whose documents are no longer available. */
defineProps<{ hunks: DiffHunk[] }>()
</script>

<template>
  <div class="ph-no-capture overflow-auto rounded-md border border-default font-mono text-xs">
    <div v-for="(hunk, index) in hunks" :key="index" class="border-b border-default last:border-b-0">
      <div class="bg-elevated px-3 py-1 text-dimmed">
        @@ -{{ hunk.OldStart }},{{ hunk.OldCount }} +{{ hunk.NewStart }},{{ hunk.NewCount }} @@
      </div>
      <div
        v-for="(line, lineIndex) in hunk.Lines ?? []"
        :key="lineIndex"
        class="grid grid-cols-[3rem_3rem_1rem_1fr] whitespace-pre"
        :class="line.Type === 'Addition' ? 'bg-success/10' : line.Type === 'Deletion' ? 'bg-error/10' : ''"
      >
        <span class="pe-2 text-end text-dimmed">{{ line.OldLineNumber ?? '' }}</span>
        <span class="pe-2 text-end text-dimmed">{{ line.NewLineNumber ?? '' }}</span>
        <span
          :class="line.Type === 'Addition' ? 'text-success' : line.Type === 'Deletion' ? 'text-error' : 'text-dimmed'"
        >
          {{ line.Type === 'Addition' ? '+' : line.Type === 'Deletion' ? '−' : ' ' }}
        </span>
        <span>{{ line.Content }}</span>
      </div>
    </div>
  </div>
</template>
