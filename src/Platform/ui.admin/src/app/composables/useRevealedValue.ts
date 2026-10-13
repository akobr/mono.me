import { onBeforeUnmount, ref } from 'vue'

/**
 * A revealed secret value: fetched on request, never cached, masked again after `hideAfterMs` (60 seconds) and when
 * the component goes away (spec section 29.8).
 */
export function useRevealedValue(fetch: () => Promise<string | undefined>, hideAfterMs = 60_000) {
  const value = ref<string>()
  const loading = ref(false)
  const error = ref<unknown>()
  let timer: ReturnType<typeof setTimeout> | undefined

  function hide() {
    value.value = undefined
    if (timer) {
      clearTimeout(timer)
      timer = undefined
    }
  }

  async function reveal(): Promise<void> {
    loading.value = true
    error.value = undefined
    try {
      value.value = await fetch()
      if (timer) {
        clearTimeout(timer)
      }
      timer = setTimeout(hide, hideAfterMs)
    } catch (caught) {
      error.value = caught
    } finally {
      loading.value = false
    }
  }

  onBeforeUnmount(hide)

  return { value, loading, error, reveal, hide }
}
