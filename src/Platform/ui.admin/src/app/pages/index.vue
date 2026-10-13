<script setup lang="ts">
import { useRouter } from 'vue-router'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'
import { accessibleContext } from '@/utils/navigation'

definePage({ meta: { layout: 'bare' } })

// "/" goes to the last context the user worked in, as far as they still have access to it; otherwise to /orgs.
const router = useRouter()
const last = accessibleContext(usePreferencesStore().state.lastContext, useSessionStore().accessMap)

const target = last?.project
  ? `/${last.org}/${last.project}/${last.view ?? 'default'}/annotations`
  : last?.org
    ? `/${last.org}`
    : '/orgs'

void router.replace(target)
</script>

<template>
  <LoadingState label="Opening…" />
</template>
