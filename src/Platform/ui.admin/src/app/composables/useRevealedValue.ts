import { onBeforeUnmount, ref } from 'vue'

/**
 * A revealed secret value: fetched on request, never cached, masked again after `hideAfterMs` (60 seconds) and when
 * the component goes away (spec section 29.8). A reveal still on its way when the value is hidden (the page moved to
 * another view, a save) is dropped when it arrives: it may be the value of what was shown before.
 */
export function useRevealedValue(fetch: () => Promise<string | undefined>, hideAfterMs = 60_000) {
  const value = ref<string>()
  const loading = ref(false)
  const error = ref<unknown>()
  let timer: ReturnType<typeof setTimeout> | undefined
  let generation = 0

  function hide() {
    generation++
    value.value = undefined
    loading.value = false
    if (timer) {
      clearTimeout(timer)
      timer = undefined
    }
  }

  /** The error of this reveal, when it failed and is still the current one; undefined otherwise. */
  async function reveal(): Promise<unknown> {
    const asked = ++generation
    loading.value = true
    error.value = undefined
    try {
      const fetched = await fetch()
      if (asked !== generation) {
        return
      }
      value.value = fetched
      if (timer) {
        clearTimeout(timer)
      }
      timer = setTimeout(hide, hideAfterMs)
    } catch (caught) {
      if (asked === generation) {
        error.value = caught
        return caught
      }
    } finally {
      if (asked === generation) {
        loading.value = false
      }
    }
  }

  onBeforeUnmount(hide)

  return { value, loading, error, reveal, hide }
}
