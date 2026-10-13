<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'
import { useEventListener } from '@vueuse/core'
import { usePreferencesStore } from '@/stores/preferences'
import { clampDetailWidth, detailWidth } from '@/utils/detail-panel'

/**
 * The detail beside a list (detail panel spec): an overlay over the list, a panel pinned beside it, or full screen
 * in its place; below md it is always full screen with a back button. It is one element in every mode, so switching
 * the mode never remounts the detail or loses an edit. The overlay and the pinned panel share one width (25-70 %),
 * resized from the panel's left border.
 */
withDefaults(defineProps<{ id?: string; title?: string }>(), { id: 'detail', title: undefined })
const emit = defineEmits<{ close: [] }>()

const preferences = usePreferencesStore()
const { mode, medium, large, track } = useDetailPanel()
track()

const panel = useTemplateRef<HTMLElement>('panel')
const width = computed(() => clampDetailWidth(preferences.state.detailWidth))
const resizable = computed(() => large.value && mode.value !== 'full')

const placement = computed(() => {
  switch (mode.value) {
    case 'overlay':
      // Below the list's navbar, so New, search and the user menu stay usable; the list stays usable on the left.
      return 'absolute right-0 bottom-0 top-(--ui-header-height) z-30 border-l border-default shadow-xl animate-[slide-in-from-right_200ms_ease-in-out]'
    case 'pinned':
      // The list before it draws the border between them.
      return 'relative shrink-0'
    default:
      return 'relative flex-1'
  }
})
const size = computed(() => {
  if (mode.value === 'full') {
    return undefined
  }
  return { width: large.value ? `${width.value}%` : '90%' }
})

function close() {
  emit('close')
}

// Resizing: the pointer is captured by the handle, and the width is the distance to the right edge of the group.
const dragging = ref(false)
let bounds: DOMRect | undefined

function startResize(event: PointerEvent) {
  const group = panel.value?.parentElement
  if (event.button !== 0 || !group) {
    return
  }
  event.preventDefault()
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
  bounds = group.getBoundingClientRect()
  dragging.value = true
}

function resize(event: PointerEvent) {
  if (dragging.value && bounds) {
    const percent = ((bounds.right - event.clientX) / bounds.width) * 100
    preferences.state.detailWidth = clampDetailWidth(Math.round(percent * 10) / 10)
  }
}

function resizeByKey(event: KeyboardEvent) {
  const step = { ArrowLeft: detailWidth.step, ArrowRight: -detailWidth.step }[event.key]
  if (step) {
    event.preventDefault()
    preferences.state.detailWidth = clampDetailWidth(width.value + step)
  }
}

// Esc closes the detail from inside the panel or from the page body, but not from a control, a menu or a dialog;
// a page that handles Esc itself (the annotation grid) prevents the default and wins.
useEventListener(window, 'keydown', (event: KeyboardEvent) => {
  if (event.key !== 'Escape' || event.defaultPrevented || event.metaKey || event.ctrlKey || event.altKey) {
    return
  }
  const target = event.target as HTMLElement | null
  if (target && target !== document.body && !panel.value?.contains(target)) {
    return
  }
  if (
    target?.closest(
      'input, textarea, select, button, a, [contenteditable="true"], [role="dialog"], [role="menu"], [role="listbox"]',
    )
  ) {
    return
  }
  event.preventDefault()
  close()
})
</script>

<template>
  <section
    :id="id"
    ref="panel"
    :aria-label="title"
    :data-detail-mode="mode"
    class="flex min-h-0 min-w-0 flex-col bg-default"
    :class="placement"
    :style="size"
  >
    <div
      v-if="resizable"
      role="separator"
      tabindex="0"
      aria-orientation="vertical"
      aria-label="Resize the detail"
      :aria-controls="id"
      :aria-valuenow="Math.round(width)"
      :aria-valuemin="detailWidth.min"
      :aria-valuemax="detailWidth.max"
      :data-dragging="dragging || undefined"
      class="group absolute inset-y-0 -left-1.5 z-10 w-3 cursor-ew-resize touch-none select-none focus-visible:outline-none"
      @pointerdown="startResize"
      @pointermove="resize"
      @lostpointercapture="dragging = false"
      @dblclick="preferences.state.detailWidth = detailWidth.default"
      @keydown="resizeByKey"
    >
      <span
        class="absolute inset-y-0 left-[5px] w-0.5 transition-colors group-hover:bg-primary group-focus-visible:bg-primary group-data-dragging:bg-primary"
      />
    </div>

    <AppNavbar v-if="mode === 'full' && medium" :title="title" />
    <slot name="header" :close="close" />
    <div class="flex min-h-0 flex-1 flex-col overflow-y-auto">
      <slot />
    </div>
  </section>
</template>
