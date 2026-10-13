import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { usePreferencesStore } from '@/stores/preferences'
import { detailMode } from '@/utils/detail-panel'

/** Detail panels on screen; while one is open in full screen, the list it belongs to is hidden. */
const shown = ref(0)

/**
 * The mode of the detail beside a list (overlay, pinned or full screen) from the remembered preferences and the
 * screen size, shared by DetailPanel, its header buttons and the list pages.
 */
export function useDetailPanel() {
  const preferences = usePreferencesStore()
  const medium = useMediaQuery('(min-width: 768px)')
  const large = useMediaQuery('(min-width: 1024px)')

  const mode = computed(() =>
    detailMode({
      pinned: preferences.state.detailPinned,
      fullscreen: preferences.state.detailFullscreen,
      medium: medium.value,
      large: large.value,
    }),
  )

  return {
    mode,
    medium,
    large,
    /** A detail is open in full screen: the list page hides its own panel. */
    covers: computed(() => shown.value > 0 && mode.value === 'full'),
    /** Pin and unpin at lg and above, outside full screen. */
    canPin: computed(() => large.value && mode.value !== 'full'),
    /** Full screen is a choice at md and above; below md the detail is always full screen. */
    canFullscreen: medium,
    togglePinned: () => (preferences.state.detailPinned = !preferences.state.detailPinned),
    toggleFullscreen: () => (preferences.state.detailFullscreen = !preferences.state.detailFullscreen),
    /** Counts the calling DetailPanel as open while it is mounted. */
    track: () => {
      onMounted(() => shown.value++)
      onBeforeUnmount(() => shown.value--)
    },
  }
}
