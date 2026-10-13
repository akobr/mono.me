<script setup lang="ts">
import { computed } from 'vue'
import { useKeyValuePolicy } from '@/queries/scope'

// The secrets settings of the project (spec 29.6): allowed levels, the keys, the customer key and the recipients.
// Every reader sees them; Administrators change them.
const canAdmin = useCan('manageAccess')
const policy = useKeyValuePolicy()
const recipients = computed(() => policy.data.value?.Recipients ?? [])
</script>

<template>
  <UDashboardPanel id="secrets">
    <template #header>
      <AppNavbar title="Secrets" />
    </template>
    <template #body>
      <div class="mx-auto w-full max-w-3xl space-y-6">
        <LoadingState v-if="policy.isPending.value" />
        <ErrorState v-else-if="policy.isError.value" :error="policy.error.value" @retry="policy.refetch()" />
        <template v-else-if="policy.data.value">
          <SecretsPolicyCard :policy="policy.data.value" :editable="canAdmin" />
          <SecretsKeysCard :policy="policy.data.value" :editable="canAdmin" />
          <ByokCard :policy="policy.data.value" :editable="canAdmin" />
          <RecipientsCard :recipients="recipients" :editable="canAdmin" />
        </template>
      </div>
    </template>
  </UDashboardPanel>
</template>
