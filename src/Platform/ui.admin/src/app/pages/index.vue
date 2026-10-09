<script setup lang="ts">
import { useRouter } from 'vue-router'
import { roleRank } from '@42for.net/2splatform.sdk'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'

definePage({ meta: { layout: 'bare' } })

// "/" goes to the last context the user worked in, when they still have access to it; otherwise to /orgs.
const router = useRouter()
const last = usePreferencesStore().state.lastContext
const accessMap = useSessionStore().accessMap

const target =
  last && roleRank(accessMap[`${last.org}.${last.project}`]) > 0
    ? `/${last.org}/${last.project}/${last.view}/annotations`
    : '/orgs'

void router.replace(target)
</script>

<template>
  <LoadingState label="Opening…" />
</template>
