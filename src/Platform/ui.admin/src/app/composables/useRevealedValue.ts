import { onBeforeUnmount, ref } from 'vue'

/**
 * A revealed secret value: fetched on request, never cached, masked again after `hideAfterMs` (60 seconds) and when
 * the component goes away (spec section 29.8). A reveal still on its way when the value is hidden (the page moved to
 * another view, a save) or asked again is aborted through the signal `fetch` gets, so its refusal for the step-up does
 * not ask on the page it no longer belongs to, and an answer that comes anyway is dropped: it may be the value of what
 * was shown before.
 */
export function useRevealedValue(fetch: (signal: AbortSignal) => Promise<string | undefined>, hideAfterMs = 60_000) {
  const value = ref<string>()
  const loading = ref(false)
  const error = ref<unknown>()
  let timer: ReturnType<typeof setTimeout> | undefined
  let generation = 0
  let request: AbortController | undefined

  function abort() {
    request?.abort()
    request = undefined
  }

  function hide() {
    generation++
    abort()
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
    abort()
    const current = new AbortController()
    request = current
    loading.value = true
    error.value = undefined
    try {
      const fetched = await fetch(current.signal)
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
        request = undefined
      }
    }
  }

  onBeforeUnmount(hide)

  return { value, loading, error, reveal, hide }
}
