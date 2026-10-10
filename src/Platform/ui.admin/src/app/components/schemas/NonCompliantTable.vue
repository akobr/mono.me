<script setup lang="ts">
import type { SchemaValidationErrorDetail } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { humanizeKind } from '@/utils/schema-validation'

/** The configurations a schema change would break (409 of Set*Schema), each linking to its configuration. */
defineProps<{ details: SchemaValidationErrorDetail[] }>()

const context = useContextStore()

const message = (text: string) => {
  const match = /^(#[^:]*):\s*(.*)$/.exec(text)
  return match ? `${match[1]!.replace(/^#/, '') || '/'} ${humanizeKind(match[2]!)}` : text
}
</script>

<template>
  <div class="overflow-auto rounded-md border border-default">
    <table class="w-full text-sm">
      <thead class="bg-elevated text-xs text-muted">
        <tr>
          <th class="px-3 py-2 text-start font-medium">Configuration</th>
          <th class="px-3 py-2 text-start font-medium">View</th>
          <th class="px-3 py-2 text-start font-medium">Errors</th>
        </tr>
      </thead>
      <tbody class="divide-y divide-default">
        <tr v-for="detail in details" :key="`${detail.ViewName}:${detail.AnnotationKey}`">
          <td class="px-3 py-2">
            <RouterLink
              :to="`/${context.org}/${context.project}/${detail.ViewName ?? context.view}/configurations/${detail.AnnotationKey}`"
              class="font-mono text-xs text-primary hover:underline"
            >
              {{ detail.AnnotationKey }}
            </RouterLink>
          </td>
          <td class="px-3 py-2 text-xs">{{ detail.ViewName }}</td>
          <td class="px-3 py-2 text-xs">
            <ul>
              <li v-for="error in detail.Errors ?? []" :key="error" class="font-mono">{{ message(error) }}</li>
            </ul>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
