<script setup lang="ts">
import { shortcutGroups } from '@/composables/useShortcutsHelp'

const { open } = useShortcutsHelp()
</script>

<template>
  <UModal v-model:open="open" title="Keyboard shortcuts" :ui="{ content: 'max-w-lg' }">
    <template #body>
      <div class="grid gap-6 sm:grid-cols-2">
        <section v-for="group in shortcutGroups" :key="group.title" class="space-y-2">
          <h3 class="text-sm font-semibold text-highlighted">{{ group.title }}</h3>
          <ul class="space-y-1.5">
            <li
              v-for="shortcut in group.shortcuts"
              :key="shortcut.description"
              class="flex items-center justify-between gap-3 text-sm"
            >
              <span class="text-muted">{{ shortcut.description }}</span>
              <span class="flex shrink-0 gap-0.5">
                <UKbd v-for="key in shortcut.keys" :key="key" :value="key" />
              </span>
            </li>
          </ul>
        </section>
      </div>
    </template>
  </UModal>
</template>
