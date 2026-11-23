import { describe, expect, it, vi } from 'vitest'
import { createLimiter } from '@/utils/limiter'

// The limiter of the reads of a comparison across views (spec "Copy and Compare Annotations and Configurations Between
// Views", section 4): a hundred keys must not be sent at once.

/** A task that the test finishes by hand, and that says when it started. */
function manualTask<T = string>(name: T, log: T[] = []) {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const result = new Promise<T>((done, fail) => {
    resolve = done
    reject = fail
  })
  let started = false
  return {
    run: () => {
      started = true
      log.push(name)
      return result
    },
    get started() {
      return started
    },
    finish: () => resolve(name),
    fail: (reason: unknown) => reject(reason),
  }
}

/** Lets the limiter hand a freed slot over: its continuations are microtasks. */
const tick = async () => {
  for (let index = 0; index < 5; index++) {
    await Promise.resolve()
  }
}

describe('createLimiter', () => {
  it('does not start a waiting task whose signal aborted between its slot and its start', async () => {
    const limit = createLimiter(1)
    let releaseFirst!: () => void
    const first = limit(() => new Promise<void>((resolve) => (releaseFirst = resolve)))

    // a signal that aborts the moment its waiting task gets the slot: the listener is gone by then
    const reason = new Error('aborted')
    let aborted = false
    const signal = {
      get aborted() {
        return aborted
      },
      reason,
      addEventListener: () => {},
      removeEventListener: () => (aborted = true),
      throwIfAborted: () => {
        if (aborted) {
          throw reason
        }
      },
    } as unknown as AbortSignal
    const task = vi.fn(async () => 'ran')
    const second = limit(task, signal)
    const third = limit(async () => 'third')

    releaseFirst()
    await first
    await expect(second).rejects.toBe(reason)
    expect(task).not.toHaveBeenCalled()
    // and its slot goes on to the next task
    await expect(third).resolves.toBe('third')
  })

  it('returns what the task returns', async () => {
    const limit = createLimiter(2)

    await expect(limit(async () => 42)).resolves.toBe(42)
    await expect(limit(() => Promise.resolve({ key: 'sbt.northwind' }))).resolves.toEqual({ key: 'sbt.northwind' })
  })

  it('runs at most as many tasks at once as its limit', async () => {
    const limit = createLimiter(4)
    let running = 0
    let most = 0
    const task = (index: number) => async () => {
      most = Math.max(most, ++running)
      // tasks of different lengths, so they finish out of their order
      await new Promise((resolve) => setTimeout(resolve, (index * 7) % 5))
      running--
      return index
    }

    const results = await Promise.all(Array.from({ length: 40 }, (_, index) => limit(task(index))))

    expect(most).toBe(4)
    expect(running).toBe(0)
    expect(results).toEqual(Array.from({ length: 40 }, (_, index) => index))
  })

  it('starts the first tasks at once and the others in their order as slots free up', async () => {
    const limit = createLimiter(2)
    const log: string[] = []
    const tasks = ['a', 'b', 'c', 'd', 'e'].map((name) => manualTask(name, log))
    const results = tasks.map((task) => limit(task.run))

    await tick()
    expect(log).toEqual(['a', 'b'])

    // b finishes first: its slot goes to the task that waits longest
    tasks[1]!.finish()
    await tick()
    expect(log).toEqual(['a', 'b', 'c'])

    tasks[0]!.finish()
    await tick()
    expect(log).toEqual(['a', 'b', 'c', 'd'])

    tasks[2]!.finish()
    tasks[3]!.finish()
    await tick()
    expect(log).toEqual(['a', 'b', 'c', 'd', 'e'])

    tasks[4]!.finish()
    await expect(Promise.all(results)).resolves.toEqual(['a', 'b', 'c', 'd', 'e'])
  })

  it('runs one task after another with a limit of one', async () => {
    const limit = createLimiter(1)
    const log: string[] = []
    const task = (name: string) => async () => {
      log.push(`${name} starts`)
      await new Promise((resolve) => setTimeout(resolve, 1))
      log.push(`${name} ends`)
    }

    await Promise.all([limit(task('a')), limit(task('b')), limit(task('c'))])

    expect(log).toEqual(['a starts', 'a ends', 'b starts', 'b ends', 'c starts', 'c ends'])
  })

  it('frees the slot of a rejected task, and rejects only that task', async () => {
    const limit = createLimiter(1)
    const failing = manualTask('failing')
    const waiting = manualTask('waiting')
    const failed = limit(failing.run)
    const waited = limit(waiting.run)
    await tick()
    expect(waiting.started).toBe(false)

    failing.fail(new Error('The read failed'))

    await expect(failed).rejects.toThrow('The read failed')
    await tick()
    expect(waiting.started).toBe(true)
    waiting.finish()
    await expect(waited).resolves.toBe('waiting')
    // the slot is free again
    await expect(limit(async () => 'next')).resolves.toBe('next')
  })

  it('frees the slot of a task that throws before it returns a promise', async () => {
    const limit = createLimiter(1)
    const thrown = limit(() => {
      throw new Error('No request was built')
    })

    await expect(thrown).rejects.toThrow('No request was built')
    await expect(limit(async () => 'next')).resolves.toBe('next')
  })

  it('rejects a waiting task whose signal aborts, without running it', async () => {
    const limit = createLimiter(1)
    const log: string[] = []
    const [first, aborted, last] = ['first', 'aborted', 'last'].map((name) => manualTask(name, log))
    const controller = new AbortController()
    const reason = new Error('The query was cancelled')

    const results = [limit(first!.run), limit(aborted!.run, controller.signal), limit(last!.run)]
    await tick()
    controller.abort(reason)

    await expect(results[1]).rejects.toBe(reason)
    // the slot of the first task is still taken, so the task behind the aborted one still waits
    expect(log).toEqual(['first'])

    first!.finish()
    await tick()
    expect(log).toEqual(['first', 'last'])
    expect(aborted!.started).toBe(false)
    last!.finish()
    await expect(results[2]).resolves.toBe('last')
  })

  it('keeps its count when waiting tasks abort: the limit holds afterwards', async () => {
    const limit = createLimiter(2)
    const log: number[] = []
    const tasks = Array.from({ length: 8 }, (_, index) => manualTask(index, log))
    const controllers = tasks.map(() => new AbortController())
    const results = tasks.map((task, index) => limit(task.run, controllers[index]!.signal))
    // an aborted task is handled, so its rejection is not reported as unhandled
    const settled = Promise.allSettled(results)
    await tick()

    // of the six that wait, the second, the fourth and the last one abort
    for (const index of [3, 5, 7]) {
      controllers[index]!.abort()
    }
    await tick()
    expect(log).toEqual([0, 1])

    tasks[0]!.finish()
    tasks[1]!.finish()
    await tick()
    expect(log).toEqual([0, 1, 2, 4])

    tasks[2]!.finish()
    await tick()
    expect(log).toEqual([0, 1, 2, 4, 6])
    // both slots are taken and nothing waits: two more tasks wait, and each start needs a task to finish
    const extra = [manualTask(8, log), manualTask(9, log)]
    const more = extra.map((task) => limit(task.run))
    await tick()
    expect(log).toEqual([0, 1, 2, 4, 6])

    tasks[4]!.finish()
    await tick()
    expect(log).toEqual([0, 1, 2, 4, 6, 8])

    for (const task of [tasks[6]!, ...extra]) {
      task.finish()
    }
    await Promise.all(more)
    expect((await settled).map((result) => result.status)).toEqual([
      'fulfilled',
      'fulfilled',
      'fulfilled',
      'rejected',
      'fulfilled',
      'rejected',
      'fulfilled',
      'rejected',
    ])
    expect(log).toEqual([0, 1, 2, 4, 6, 8, 9])
  })

  it('rejects at once for a signal that is already aborted, and takes no slot for it', async () => {
    const limit = createLimiter(1)
    const never = manualTask('never')
    const reason = new Error('Cancelled before it started')

    await expect(limit(never.run, AbortSignal.abort(reason))).rejects.toBe(reason)
    expect(never.started).toBe(false)

    // the one slot is free
    const next = manualTask('next')
    const result = limit(next.run)
    await tick()
    expect(next.started).toBe(true)
    next.finish()
    await expect(result).resolves.toBe('next')
  })

  it('rejects with the reason of an abort without one', async () => {
    const limit = createLimiter(1)
    const first = manualTask('first')
    const controller = new AbortController()
    const held = limit(first.run)
    const waiting = limit(async () => 'never', controller.signal)
    await tick()

    controller.abort()

    await expect(waiting).rejects.toMatchObject({ name: 'AbortError' })
    first.finish()
    await held
  })

  it('leaves a task that has started to its own signal', async () => {
    const limit = createLimiter(1)
    const task = manualTask('started')
    const controller = new AbortController()
    const result = limit(task.run, controller.signal)
    await tick()

    // the read itself gets the signal and decides; the limiter only stops what still waits
    controller.abort()
    task.finish()

    await expect(result).resolves.toBe('started')
    await expect(limit(async () => 'next')).resolves.toBe('next')
  })

  it('does not listen to the signal of a task once it has started', async () => {
    const limit = createLimiter(1)
    const first = manualTask('first')
    const second = manualTask('second')
    const controller = new AbortController()
    const results = [limit(first.run), limit(second.run, controller.signal)]
    await tick()

    first.finish()
    await tick()
    expect(second.started).toBe(true)
    // an abort now must neither reject the running task through the limiter nor give its slot away twice
    controller.abort()
    const third = manualTask('third')
    const waiting = limit(third.run)
    await tick()
    expect(third.started).toBe(false)

    second.finish()
    await expect(results[1]).resolves.toBe('second')
    await tick()
    expect(third.started).toBe(true)
    third.finish()
    await waiting
  })

  it('runs a thousand tasks through four slots', async () => {
    const limit = createLimiter(4)
    let running = 0
    let most = 0

    const results = await Promise.all(
      Array.from({ length: 1000 }, (_, index) =>
        limit(async () => {
          most = Math.max(most, ++running)
          await Promise.resolve()
          running--
          return index
        }),
      ),
    )

    expect(most).toBe(4)
    expect(results).toHaveLength(1000)
    expect(results.at(-1)).toBe(999)
  })

  it('keeps the slots of two limiters apart', async () => {
    const [one, other] = [createLimiter(1), createLimiter(1)]
    const held = manualTask('held')
    const holding = one(held.run)
    await tick()

    await expect(other(async () => 'free')).resolves.toBe('free')

    held.finish()
    await holding
  })
})
