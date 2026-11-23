/** Runs a task when a slot is free; a signal that aborts while the task waits rejects it without running it. */
export type Limiter = <T>(task: () => Promise<T>, signal?: AbortSignal) => Promise<T>

/**
 * At most `limit` tasks at once; the others start in their order as slots free up. A read of the API times out 30
 * seconds after it is sent, the browser's own queue included, so many reads are not sent at once (spec "Copy and
 * Compare Annotations and Configurations Between Views", section 4).
 */
export function createLimiter(limit: number): Limiter {
  let running = 0
  const waiting: (() => void)[] = []

  return async (task, signal) => {
    signal?.throwIfAborted()

    if (running < limit) {
      running++
    } else {
      // The slot of a finished task is handed over, so the count stays where it is.
      await new Promise<void>((resolve, reject) => {
        const start = () => {
          signal?.removeEventListener('abort', abort)
          resolve()
        }
        const abort = () => {
          const index = waiting.indexOf(start)
          if (index >= 0) {
            waiting.splice(index, 1)
          }
          reject(signal!.reason as Error)
        }
        waiting.push(start)
        signal?.addEventListener('abort', abort, { once: true })
      })
    }

    try {
      // The slot may have been handed over after the signal aborted: a task that has not started does not start.
      signal?.throwIfAborted()
      return await task()
    } finally {
      const next = waiting.shift()
      if (next) {
        next()
      } else {
        running--
      }
    }
  }
}
