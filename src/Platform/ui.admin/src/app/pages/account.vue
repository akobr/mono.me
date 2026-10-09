<script setup lang="ts">
import { computed } from 'vue'
import { useColorMode } from '@vueuse/core'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'
import { groupMemberships } from '@/utils/memberships'

const session = useSessionStore()
const preferences = usePreferencesStore()
const { store: colorMode } = useColorMode({ emitAuto: true })

const memberships = computed(() =>
  groupMemberships(session.accessMap).flatMap((organization) => [
    ...(organization.role
      ? [{ key: organization.name, kind: 'Organization', role: organization.role, to: `/${organization.name}` }]
      : []),
    ...organization.projects.map((project) => ({
      key: project.key,
      kind: 'Project',
      role: project.role,
      to: `/${project.org}/${project.name}/default/annotations`,
    })),
  ]),
)

const colorModes = [
  { label: 'Light', value: 'light', icon: 'i-lucide-sun' },
  { label: 'Dark', value: 'dark', icon: 'i-lucide-moon' },
  { label: 'System', value: 'auto', icon: 'i-lucide-monitor' },
]
const editorFormats = [
  { label: 'JSON', value: 'json' },
  { label: 'YAML', value: 'yaml' },
]
const annotationModes = [
  { label: 'Form', value: 'form' },
  { label: 'JSON', value: 'json' },
  { label: 'YAML', value: 'yaml' },
]
const densities = [
  { label: 'Compact', value: 'compact' },
  { label: 'Comfortable', value: 'comfortable' },
]
</script>

<template>
  <UDashboardPanel id="account">
    <template #header>
      <AppNavbar title="Account" />
    </template>

    <template #body>
      <div class="mx-auto w-full max-w-3xl space-y-6">
        <UCard>
          <div class="flex items-center gap-4">
            <UAvatar :src="session.user?.pictureUrl" :alt="session.user?.name" size="xl" />
            <div class="min-w-0 space-y-0.5">
              <p class="truncate text-lg font-semibold text-highlighted">{{ session.user?.name }}</p>
              <p class="truncate text-sm text-muted">{{ session.user?.email }}</p>
              <p class="flex items-center gap-1 font-mono text-xs text-dimmed">
                {{ session.account?.Id ?? session.user?.id }}
                <CopyButton v-if="session.account?.Id" :value="session.account.Id" />
              </p>
            </div>
            <UButton
              class="ms-auto"
              label="Sign out"
              icon="i-lucide-log-out"
              color="neutral"
              variant="outline"
              @click="session.signOut()"
            />
          </div>
        </UCard>

        <UCard :ui="{ body: 'p-0 sm:p-0' }">
          <template #header>
            <h2 class="font-semibold">Memberships</h2>
          </template>
          <ul class="divide-y divide-default">
            <li v-for="membership in memberships" :key="membership.key">
              <RouterLink
                :to="membership.to"
                class="flex items-center justify-between gap-3 px-4 py-2.5 text-sm hover:bg-elevated/50"
              >
                <span class="flex min-w-0 items-center gap-2">
                  <UIcon
                    :name="membership.kind === 'Organization' ? 'i-lucide-building-2' : 'i-lucide-folder'"
                    class="size-4 shrink-0 text-muted"
                  />
                  <span class="truncate font-mono">{{ membership.key }}</span>
                  <span class="text-xs text-dimmed">{{ membership.kind }}</span>
                </span>
                <RoleBadge :role="membership.role" />
              </RouterLink>
            </li>
            <li v-if="memberships.length === 0" class="px-4 py-3 text-sm text-muted">No memberships.</li>
          </ul>
        </UCard>

        <UCard>
          <template #header>
            <h2 class="font-semibold">Preferences</h2>
          </template>
          <div class="grid gap-5 sm:grid-cols-2">
            <UFormField label="Theme">
              <UTabs v-model="colorMode" :items="colorModes" :content="false" size="sm" />
            </UFormField>
            <UFormField label="Row density">
              <UTabs v-model="preferences.state.density" :items="densities" :content="false" size="sm" />
            </UFormField>
            <UFormField label="Document format" help="Default for configuration, template and schema editors.">
              <UTabs v-model="preferences.state.editorFormat" :items="editorFormats" :content="false" size="sm" />
            </UFormField>
            <UFormField label="Annotation editing" help="Default mode of the annotation detail.">
              <UTabs v-model="preferences.state.annotationMode" :items="annotationModes" :content="false" size="sm" />
            </UFormField>
          </div>
          <template #footer>
            <div class="flex justify-end">
              <UButton
                label="Reset preferences"
                color="neutral"
                variant="ghost"
                size="sm"
                @click="preferences.reset()"
              />
            </div>
          </template>
        </UCard>
      </div>
    </template>
  </UDashboardPanel>
</template>
