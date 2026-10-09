<script setup lang="ts">
import { computed } from 'vue'
import { useBootState } from '@/boot/state'

definePage({ meta: { public: true, layout: 'bare' } })

const boot = useBootState()

const content = computed(() => {
  const state = boot.value

  if (state.status === 'unsupported') {
    return {
      title: 'Sign-in provider not supported',
      description: `This deployment uses ${state.provider}; the admin UI supports WorkOS AuthKit only. Use the sform CLI instead.`,
    }
  }

  if (state.status === 'error') {
    return { title: 'The admin UI cannot start', description: state.message }
  }

  return { title: 'Everything is fine', description: 'The application is configured.' }
})

const reload = () => location.reload()
</script>

<template>
  <UCard>
    <div class="space-y-4">
      <div class="flex items-start gap-3">
        <UIcon name="i-lucide-plug-zap" class="mt-0.5 size-6 shrink-0 text-warning" />
        <div class="space-y-1">
          <h1 class="text-lg font-semibold text-highlighted">{{ content.title }}</h1>
          <p class="text-sm text-muted">{{ content.description }}</p>
        </div>
      </div>
      <div class="flex justify-end gap-2">
        <UButton v-if="boot.status === 'ready'" to="/" label="Open the app" />
        <UButton v-else label="Try again" icon="i-lucide-rotate-cw" color="neutral" variant="outline" @click="reload" />
      </div>
    </div>
  </UCard>
</template>
