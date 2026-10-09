<script setup lang="ts">
import { onBeforeUnmount } from 'vue'
import { onApiError } from '@/boot/api-errors'

// Global toasts for server errors and an unreachable API; screens handle 4xx themselves.
const toast = useToast()

const unsubscribe = onApiError((notice) => {
  toast.add({
    id: `api-error:${notice.title}`,
    title: notice.title,
    description: notice.description,
    color: 'error',
    icon: 'i-lucide-cloud-alert',
  })
})

onBeforeUnmount(unsubscribe)
</script>

<template>
  <span hidden />
</template>
