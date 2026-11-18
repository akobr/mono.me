<script setup lang="ts">
// The access audit of the organization (424-263): every change of members, projects, machines, policies, trusted
// issuers and shared certificates of the organization and its projects. `audit` is a reserved project name, so no new
// project can be hidden by this path; a project named `audit` created before the name was reserved loses its landing
// page here, while its views and other pages stay reachable. Only the organization's Owners and Administrators read
// it; below them the page shows why and asks for nothing.
const canAdmin = useCan('manageAccess', 'org')
</script>

<template>
  <!-- the table fills the panel and scrolls itself, below its filter toolbar -->
  <UDashboardPanel id="organization-audit" :ui="{ body: canAdmin ? 'p-0 sm:p-0 gap-0 overflow-hidden' : undefined }">
    <template #header>
      <AppNavbar title="Audit" />
    </template>
    <template #body>
      <EmptyState
        v-if="!canAdmin"
        icon="i-lucide-lock"
        title="Administrators only"
        description="The access audit trail of the organization is visible to its Owners and Administrators."
      />
      <AccessAuditTrail v-else />
    </template>
  </UDashboardPanel>
</template>
