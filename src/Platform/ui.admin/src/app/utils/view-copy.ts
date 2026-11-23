import type { QueryClient } from '@tanstack/vue-query'
import {
  findDeletedAnnotations,
  getAnnotationVersioned,
  getViews,
  hasErrorCode,
  isConflict,
  isNotFound,
  isPreconditionFailed,
  isStorytellerError,
  patchConfiguration,
  setAnnotationVersioned,
  setConfiguration,
  type Annotation,
} from '@42for.net/2splatform.sdk'
import { invalidateAllConfigurations, invalidateAnnotation, invalidateConfiguration } from '@/queries/invalidation'
import type { Scope } from '@/stores/context'
import { getAllAncestors, metaOf } from '@/utils/annotation-key'
import { errorMessage } from '@/utils/errors'
import { parseServerErrors } from '@/utils/schema-validation'
import {
  annotationText,
  canonicalJson,
  hasInexactNumber,
  readAnnotation,
  readConfiguration,
  type ViewItemKind,
} from '@/utils/view-documents'

// The copy of annotations or configurations from the view of the route (the source) to another view of the project
// (the target), key by key through the calls every client has (spec "Copy and Compare Annotations and Configurations
// Between Views", section 7).

export type ViewCopyOutcome = 'Created' | 'Updated' | 'Unchanged' | 'Skipped' | 'Failed'

export const viewCopyOutcomes: readonly ViewCopyOutcome[] = ['Created', 'Updated', 'Unchanged', 'Skipped', 'Failed']

export interface ViewCopyItem {
  kind: 'Annotation' | 'Configuration'
  key: string
  outcome: ViewCopyOutcome
  /** Why it was skipped or failed, or what a created annotation was needed for. */
  message?: string
  /** A failed row whose write reached the target all the same, for example a first write merged into another. */
  written?: true
}

export interface ViewCopyRequest {
  source: Scope
  /** The name of the target view. */
  target: string
  kind: ViewItemKind
  keys: readonly string[]
  /** What happens to a document that the target has with another content. */
  existing: 'overwrite' | 'skip'
  /** Configurations: copy an annotation that the target lacks, with its configuration. */
  missingAnnotations: boolean
  /**
   * What the comparison page saw in the target, by key: the stamp of the annotation or the version of the
   * configuration (`CompareFile.expected`). A key whose target is not that any more is skipped, unless the target
   * holds the content of the source by now: then it is unchanged.
   */
  expected?: Readonly<Record<string, string | number | null>>
  /**
   * What a check read in the source, by key (`ViewCopyResult.sources` of the check): a key whose source is not that
   * any more is skipped, so a copy writes only what its check showed.
   */
  checked?: Readonly<Record<string, string | null>>
  /**
   * What a check read in the target, by key (`ViewCopyResult.targets` of the check): a key whose target is not that
   * any more is skipped, unless the target holds the content of the source by now, so a copy to production writes
   * nothing that its check did not show.
   */
  checkedTargets?: Readonly<Record<string, string | number | null>>
  /**
   * What a check read in the source of each annotation that it would create for another key (`ViewCopyResult.
   * prerequisites` of the check): a missing ancestor, or the annotation of a configuration. One that changed since is
   * not created, and nothing below it is written.
   */
  checkedPrerequisites?: Readonly<Record<string, string>>
  /** Decide and report, write nothing. */
  dryRun?: boolean
}

export interface ViewCopyResult {
  target: string
  dryRun: boolean
  /** The selected documents and the annotations created for them, in the order they were handled. */
  items: ViewCopyItem[]
  /** The signal ended the run before every key was handled. */
  stopped: boolean
  /** Why the run ended as a whole; the items hold what was handled before. */
  error?: unknown
  /**
   * The text of the source document of each selected key that the run read (`annotationText`, or `canonicalJson` of
   * the own configuration), null for none. A copy after a check passes those of the check as `checked`.
   */
  sources: Record<string, string | null>
  /**
   * The stamp of the target document of each selected key that the run read: `annotationStamp` of the annotation,
   * the version of the configuration, null for none. A copy after a check passes those of the check as
   * `checkedTargets`.
   */
  targets: Record<string, string | number | null>
  /**
   * The source text (`annotationText`) of each annotation that the run created, or would create, for another key. A
   * copy after a check passes those of the check as `checkedPrerequisites`.
   */
  prerequisites: Record<string, string>
}

export interface ViewCopyOptions {
  /** The queries of what was written are invalidated in it. */
  client: QueryClient
  onProgress?: (done: number, total: number) => void
  /** Checked before each key: an aborted run finishes the key under way and skips the others. */
  signal?: AbortSignal
}

/** What copies a number that the browser cannot hold exactly. */
const exactTools = 'Copy it with Export & Import or sform.'
const inexactMessage = `It holds a number beyond 2^53. ${exactTools}`
const changedMessage = (target: string) => `It changed in ${target} meanwhile. Copy it again.`
const comparedMessage = (target: string) => `It changed in ${target} since it was compared.`
const checkedMessage = (view: string) => `It changed in ${view} since it was checked.`
const uncheckedMessage = 'The check did not show what a copy would do with it. Check again.'

/**
 * How a value that the run read compares with what the check read: a copy after a check writes only what the check
 * showed, so a key that the check did not get to (it failed there, or it was not needed) is not written either.
 */
type Seen = 'same' | 'changed' | 'unchecked'
function seenBy<T>(checked: Readonly<Record<string, T>> | undefined, key: string, value: T): Seen {
  if (!checked) {
    return 'same'
  }
  if (!(key in checked)) {
    return 'unchecked'
  }
  return checked[key] === value ? 'same' : 'changed'
}
export const stoppedMessage = 'Not copied, the copy was stopped.'

const mergeOrder = (key: string) => metaOf(key.slice(0, key.indexOf('.')))?.mergeOrder ?? 99

/** Ancestors before descendants; the keys of one type in key order. */
const inMergeOrder = (keys: Iterable<string>) =>
  [...new Set(keys)].toSorted((a, b) => mergeOrder(a) - mergeOrder(b) || a.localeCompare(b))

/** The stamp the comparison page keeps of a target annotation (`CompareFile.expected`). */
export const annotationStamp = (annotation: Annotation | undefined): string | null =>
  annotation?.Modified?.At ?? annotation?.Created?.At ?? null

/** A sign-in that ended or a role that is gone fails every key the same way, so it ends the run. */
const endsRun = (error: unknown) => isStorytellerError(error) && (error.status === 401 || error.status === 403)

function failureMessage(error: unknown, key: string, target: string): string {
  if (isConflict(error) && error.schemaErrors) {
    const issue = parseServerErrors(error.schemaErrors, key)[0]
    return `It does not satisfy the schema of ${target}${issue ? `: ${issue.pointer || '/'} ${issue.message}` : '.'}`
  }
  if (
    isPreconditionFailed(error) ||
    hasErrorCode(error, 'PatchTestFailed') ||
    hasErrorCode(error, 'AnnotationPreconditionFailed')
  ) {
    return changedMessage(target)
  }
  return errorMessage(error)
}

/**
 * Copies the keys of the request from its source view to its target view, one after another, ancestors first.
 * A failure of one key is a row of the result; only a failure that every key would meet (the views or the deleted
 * keys cannot be read, the target is no view, the session ended) ends the run, with `error` and the rows so far.
 */
export async function copyToView(request: ViewCopyRequest, options: ViewCopyOptions): Promise<ViewCopyResult> {
  const { source, target, existing } = request
  const { client, signal } = options
  const dryRun = !!request.dryRun
  const targetScope: Scope = { ...source, view: target }
  const result: ViewCopyResult = {
    target,
    dryRun,
    items: [],
    stopped: false,
    sources: {},
    targets: {},
    prerequisites: {},
  }
  const keys = inMergeOrder(request.keys)

  /** Whether the target has the annotation: read once, then kept up to date by the writes of the run. */
  const inTarget = new Map<string, boolean>()
  /** Annotations that the run did not bring to the target: nothing below them is written. */
  const notCopied = new Set<string>()
  const deleted = new Set<string>()
  /** Annotations that a check would create: their configurations read as none until then. */
  const planned = new Set<string>()
  let wrote = false

  /**
   * Records what the run read in the source for a selected key, and whether it is what the check read. A copy writes
   * only what its check showed: a source changed since is skipped.
   */
  function sourceRead(key: string, text: string | null): Seen {
    result.sources[key] = text
    return seenBy(request.checked, key, text)
  }

  /** Records the stamp of the target that the run read for a selected key, and how it compares with the check. */
  function targetRead(key: string, stamp: string | number | null): Seen {
    result.targets[key] = stamp
    return seenBy(request.checkedTargets, key, stamp)
  }

  /** Records the source of an annotation that the run creates for another key, and how it compares with the check. */
  function prerequisiteRead(annotation: Annotation): Seen {
    const text = annotationText(annotation)
    result.prerequisites[annotation.AnnotationKey] = text
    return seenBy(request.checkedPrerequisites, annotation.AnnotationKey, text)
  }

  const seenMessage = (seen: Seen, view: string) => (seen === 'changed' ? checkedMessage(view) : uncheckedMessage)

  /**
   * Looks the key up among the deleted ones of the target right before it is created. The lookup at the start of the
   * run is older than this write by every key before it, and a create under If-None-Match: * re-creates a deleted key
   * with an empty configuration. The window that is left is the one between this lookup and the write.
   */
  async function deletedNow(key: string): Promise<boolean> {
    if (dryRun) {
      return false
    }
    const found = await findDeletedAnnotations(source.org, source.project, target, { Keys: [key] })
    if (found.some((annotation) => annotation.AnnotationKey === key)) {
      deleted.add(key)
      return true
    }
    return false
  }

  const add = (kind: ViewCopyItem['kind'], key: string, outcome: ViewCopyOutcome, message?: string) =>
    void result.items.push({ kind, key, outcome, ...(message ? { message } : {}) })

  async function readTarget(key: string) {
    try {
      const versioned = await getAnnotationVersioned(source.org, source.project, target, key, { includeInactive: true })
      inTarget.set(key, true)
      return versioned
    } catch (error) {
      if (isNotFound(error)) {
        inTarget.set(key, false)
        return undefined
      }
      throw error
    }
  }

  const targetHas = async (key: string) => inTarget.get(key) ?? !!(await readTarget(key))

  async function writeAnnotation(annotation: Annotation, condition: { ifMatch?: string; ifNoneMatch?: string }) {
    const key = annotation.AnnotationKey
    if (!dryRun) {
      await setAnnotationVersioned(source.org, source.project, target, key, annotation, condition)
      wrote = true
      void invalidateAnnotation(client, targetScope, key)
    } else if (condition.ifNoneMatch) {
      planned.add(key)
    }
    inTarget.set(key, true)
  }

  /** The first of the key's ancestors and the key itself that the target has deleted. */
  const deletedAbove = (key: string) => [...inMergeOrder(getAllAncestors(key)), key].find((each) => deleted.has(each))

  /**
   * Creates an annotation that the target lacks, after the ancestors that it lacks too. They are copied from the
   * source, so the target gets the real ones, not the skeletons that the API creates with the dates, the time zone,
   * the disabled flag and the labels of the written descendant. Returns whether the target has the annotation now.
   */
  async function create(annotation: Annotation, reason?: string): Promise<boolean> {
    const key = annotation.AnnotationKey

    for (const ancestor of inMergeOrder(getAllAncestors(key))) {
      if (!notCopied.has(ancestor) && (await targetHas(ancestor))) {
        continue
      }
      if (!notCopied.has(ancestor)) {
        const parent = (await readAnnotation(source, ancestor)).annotation
        if (!parent) {
          // The source lacks it too: the API creates it with the descendant, as it did in the source.
          continue
        }
        const seen = prerequisiteRead(parent)
        if (seen !== 'same') {
          notCopied.add(ancestor)
          add('Annotation', ancestor, 'Skipped', seenMessage(seen, source.view))
          notCopied.add(key)
          add('Annotation', key, 'Skipped', `Its ancestor ${ancestor} was not copied.`)
          return false
        }
        try {
          if (hasInexactNumber(parent.Values)) {
            throw new Error(inexactMessage)
          }
          if (await deletedNow(ancestor)) {
            notCopied.add(ancestor)
            add('Annotation', ancestor, 'Skipped', `${ancestor} is deleted in ${target}. Restore it there first.`)
            notCopied.add(key)
            add('Annotation', key, 'Skipped', `Its ancestor ${ancestor} was not copied.`)
            return false
          }
          await writeAnnotation(parent, { ifNoneMatch: '*' })
          add('Annotation', ancestor, 'Created', `Missing ancestor of ${key}.`)
          continue
        } catch (error) {
          if (endsRun(error)) {
            throw error
          }
          notCopied.add(ancestor)
          add('Annotation', ancestor, 'Failed', failureMessage(error, ancestor, target))
        }
      }
      notCopied.add(key)
      add('Annotation', key, 'Skipped', `Its ancestor ${ancestor} was not copied.`)
      return false
    }

    if (await deletedNow(key)) {
      notCopied.add(key)
      add('Annotation', key, 'Skipped', `${key} is deleted in ${target}. Restore it there first.`)
      return false
    }
    await writeAnnotation(annotation, { ifNoneMatch: '*' })
    add('Annotation', key, 'Created', reason)
    return true
  }

  /** Brings one annotation to the target; false when the target does not have it afterwards. */
  async function copyAnnotation(key: string, role: { selected: boolean; reason?: string }): Promise<boolean> {
    const blocked = deletedAbove(key)
    if (blocked) {
      notCopied.add(key)
      add('Annotation', key, 'Skipped', `${blocked} is deleted in ${target}. Restore it there first.`)
      return false
    }

    const annotation = (await readAnnotation(source, key)).annotation
    const sourceSeen = role.selected ? sourceRead(key, annotation ? annotationText(annotation) : null) : 'same'
    if (sourceSeen !== 'same') {
      notCopied.add(key)
      add('Annotation', key, 'Skipped', seenMessage(sourceSeen, source.view))
      return false
    }
    if (!annotation) {
      notCopied.add(key)
      add('Annotation', key, 'Skipped', `It is no longer in ${source.view}.`)
      return false
    }

    const stored = await readTarget(key)
    if (stored && !role.selected) {
      // It appeared since the run looked for it. A copy of configurations creates the annotations that are missing;
      // it never changes one that the target has.
      return true
    }
    const same = !!stored && annotationText(annotation) === annotationText(stored.value)
    const stamp = annotationStamp(stored?.value)
    const moved = role.selected && !!request.expected && key in request.expected && request.expected[key] !== stamp
    const targetSeen = role.selected ? targetRead(key, stamp) : 'same'
    const movedSinceCheck = targetSeen !== 'same'
    if (moved || movedSinceCheck) {
      // The comparison page or the check showed something else: the section reads the target again, whatever the
      // run does.
      void invalidateAnnotation(client, targetScope, key)
    }
    // What the page and the check showed guards a write only: a target that changed since, and now holds what the
    // source has, is unchanged.
    if ((moved || movedSinceCheck) && !same) {
      if (!stored) {
        notCopied.add(key)
      }
      add('Annotation', key, 'Skipped', moved ? comparedMessage(target) : seenMessage(targetSeen, target))
      return !!stored
    }

    try {
      if (hasInexactNumber(annotation.Values)) {
        throw new Error(inexactMessage)
      }
      if (!stored) {
        // The annotation of a configuration is created only as the check read it.
        const seen = role.selected ? 'same' : prerequisiteRead(annotation)
        if (seen !== 'same') {
          notCopied.add(key)
          add('Annotation', key, 'Skipped', seenMessage(seen, source.view))
          return false
        }
        return await create(annotation, role.reason)
      }
      if (same) {
        add('Annotation', key, 'Unchanged')
      } else if (existing === 'skip') {
        add('Annotation', key, 'Skipped', `It is already in ${target}.`)
      } else {
        // Only the version that was read: a change made meanwhile fails with 412. Without an ETag (it is not
        // readable across origins unless the API exposes it) any existing version: either way nothing is re-created.
        await writeAnnotation(annotation, { ifMatch: stored.etag ?? '*' })
        add('Annotation', key, 'Updated')
      }
      return true
    } catch (error) {
      if (endsRun(error)) {
        throw error
      }
      if (!stored) {
        notCopied.add(key)
      }
      add('Annotation', key, 'Failed', failureMessage(error, key, target))
      return !!stored
    }
  }

  async function copyConfiguration(key: string): Promise<void> {
    const blocked = deletedAbove(key)
    if (blocked) {
      add('Configuration', key, 'Skipped', `${blocked} is deleted in ${target}. Restore it there first.`)
      return
    }

    const document = (await readConfiguration(source, key)).own
    const sourceSeen = sourceRead(key, document ? canonicalJson(document) : null)
    if (sourceSeen !== 'same') {
      add('Configuration', key, 'Skipped', seenMessage(sourceSeen, source.view))
      return
    }
    if (!document) {
      add('Configuration', key, 'Skipped', `${source.view} has no own configuration for it.`)
      return
    }
    if (hasInexactNumber(document)) {
      add('Configuration', key, 'Failed', inexactMessage)
      return
    }
    // Only the root: the API runs the directives of the root member only, and keeps a nested one as data.
    if ('$remove' in document || '$patch' in document) {
      add(
        'Configuration',
        key,
        'Failed',
        'It has a member named $remove or $patch, which a first write would run as a directive.',
      )
      return
    }

    // A configuration is never written for a key without an annotation: the API would store it as it is.
    if (notCopied.has(key) || !(await targetHas(key))) {
      if (notCopied.has(key)) {
        add('Configuration', key, 'Skipped', 'Its annotation was not copied.')
        return
      }
      if (!request.missingAnnotations) {
        // None is read: a copy after this check skips the key if the annotation appears in the target meanwhile.
        targetRead(key, null)
        add('Configuration', key, 'Skipped', `Its annotation is not in ${target}.`)
        return
      }
      // Without its annotation the target has no configuration for the key (version 0). What the comparison page or
      // the check showed is checked before the annotation and its ancestors are created, so that a configuration that
      // is then skipped leaves no annotation behind.
      const moved = !!request.expected && key in request.expected && request.expected[key] !== 0
      const seen = seenBy(request.checkedTargets, key, 0)
      if (moved || seen !== 'same') {
        void invalidateConfiguration(client, targetScope, key)
        add('Configuration', key, 'Skipped', moved ? comparedMessage(target) : seenMessage(seen, target))
        return
      }
      if (!(await copyAnnotation(key, { selected: false, reason: 'Needed by its configuration.' }))) {
        add('Configuration', key, 'Skipped', 'Its annotation was not copied.')
        return
      }
    }

    // In a dry run an annotation that would be created is not there yet: its configuration reads as none.
    const stored = await readConfiguration(targetScope, key)
    const same = !!stored.own && canonicalJson(stored.own) === canonicalJson(document)
    const moved = !!request.expected && key in request.expected && request.expected[key] !== stored.version
    const targetSeen = targetRead(key, stored.version)
    const movedSinceCheck = targetSeen !== 'same'
    if (moved || movedSinceCheck) {
      void invalidateConfiguration(client, targetScope, key)
    }
    if ((moved || movedSinceCheck) && !same) {
      add('Configuration', key, 'Skipped', moved ? comparedMessage(target) : seenMessage(targetSeen, target))
      return
    }

    // The annotation went from the target since the run found it there: a write would store a configuration without
    // an annotation. A check reads none for an annotation it would create.
    if (!stored.readable && !(dryRun && planned.has(key))) {
      add('Configuration', key, 'Skipped', `Its annotation is not in ${target} any more.`)
      return
    }

    try {
      if (!stored.own && stored.version > 0) {
        // A written version without members: the patch of a whole document replaces it, and its test fails with
        // 412 when the target got members since it was read.
        if (!dryRun) {
          await patchConfiguration(source.org, source.project, target, key, [
            { op: 'test', path: '', value: {} },
            { op: 'replace', path: '', value: document },
          ])
        }
        add('Configuration', key, 'Created')
      } else if (!stored.own) {
        if (!dryRun) {
          // Nothing is written yet, and PATCH needs a written version: POST replaces a missing document, but it has
          // no condition and merges into one that another client wrote meanwhile. Read back, that shows.
          await setConfiguration(source.org, source.project, target, key, document)
          // Written: whatever the read back says, the target changed and its queries read again.
          wrote = true
          void invalidateConfiguration(client, targetScope, key)
          let written
          try {
            written = (await readConfiguration(targetScope, key)).own
          } catch (error) {
            if (endsRun(error)) {
              throw error
            }
            result.items.push({
              kind: 'Configuration',
              key,
              outcome: 'Failed',
              message: `It was written to ${target}, but it could not be read back: ${errorMessage(error)}`,
              written: true,
            })
            return
          }
          if (!written || canonicalJson(written) !== canonicalJson(document)) {
            result.items.push({
              kind: 'Configuration',
              key,
              outcome: 'Failed',
              message: `${target} got its own configuration meanwhile, and the copy was merged into it. Check it there.`,
              written: true,
            })
            return
          }
        }
        add('Configuration', key, 'Created')
      } else if (same) {
        add('Configuration', key, 'Unchanged')
        return
      } else if (existing === 'skip') {
        add('Configuration', key, 'Skipped', `${target} has its own configuration.`)
        return
      } else if (hasInexactNumber(stored.own)) {
        // The test of the patch below would hold the rounded number, and fail with 412 on every try.
        add('Configuration', key, 'Failed', `Its configuration in ${target} holds a number beyond 2^53. ${exactTools}`)
        return
      } else {
        if (!dryRun) {
          // The whole document, as sform and the MCP server replace one: a copy replaces it as a whole, and an API
          // on JsonPatch.Net before 5.1.0 took the ~0 and ~1 of a JSON Pointer literally, so a patch by member wrote a
          // wrong member for a name with "/" or "~". The test fails with 412 when the target changed since it was read.
          await patchConfiguration(source.org, source.project, target, key, [
            { op: 'test', path: '', value: stored.own },
            { op: 'replace', path: '', value: document },
          ])
        }
        add('Configuration', key, 'Updated')
      }
      if (!dryRun) {
        wrote = true
        void invalidateConfiguration(client, targetScope, key)
      }
    } catch (error) {
      if (endsRun(error)) {
        throw error
      }
      add('Configuration', key, 'Failed', failureMessage(error, key, target))
    }
  }

  let done = 0
  try {
    // A write to any view name succeeds and creates that view, so the target must be one the project lists.
    const views = await getViews(source.org, source.project)
    if (target === source.view || !views.some((view) => view.Name === target)) {
      throw new Error(`${target} is not a view of this project.`)
    }

    // A write to a deleted key, or below a deleted ancestor, re-creates it and empties its configuration.
    const lookup = inMergeOrder(keys.flatMap((key) => [...getAllAncestors(key), key]))
    for (let index = 0; index < lookup.length; index += 100) {
      const found = await findDeletedAnnotations(source.org, source.project, target, {
        Keys: lookup.slice(index, index + 100),
      })
      found.forEach((annotation) => deleted.add(annotation.AnnotationKey))
    }

    options.onProgress?.(0, keys.length)
    for (const key of keys) {
      if (signal?.aborted) {
        result.stopped = true
        break
      }
      try {
        if (request.kind === 'annotations') {
          await copyAnnotation(key, { selected: true })
        } else {
          await copyConfiguration(key)
        }
      } catch (error) {
        if (endsRun(error)) {
          throw error
        }
        // A read failed: the key is not handled, the others are.
        add(request.kind === 'annotations' ? 'Annotation' : 'Configuration', key, 'Failed', errorMessage(error))
      }
      done++
      options.onProgress?.(done, keys.length)
    }
  } catch (error) {
    result.error = error
  }

  if (result.stopped) {
    const kind = request.kind === 'annotations' ? 'Annotation' : 'Configuration'
    keys.slice(done).forEach((key) => add(kind, key, 'Skipped', stoppedMessage))
  }

  if (wrote) {
    // The effective document of a key holds the configurations of its ancestors, and an annotation that was created
    // makes its configuration readable: every configuration of the target may read differently now. The result does
    // not wait for it: the promise settles only when every open query of the target has read again (424-166).
    void invalidateAllConfigurations(client, targetScope).catch(() => undefined)
  }
  return result
}

/** How many documents of the result had each outcome. */
export function copyCounts(items: readonly ViewCopyItem[]): Record<ViewCopyOutcome, number> {
  const counts = Object.fromEntries(viewCopyOutcomes.map((outcome) => [outcome, 0])) as Record<ViewCopyOutcome, number>
  for (const item of items) {
    counts[item.outcome]++
  }
  return counts
}

/** The documents a copy writes (or a check would): the created and the updated ones. */
export const copyWrites = (items: readonly ViewCopyItem[]): number => {
  const counts = copyCounts(items)
  return counts.Created + counts.Updated
}

/** Whether a copy changed the target: a document written, also one whose row failed after its write. */
export const copyChangedTarget = (items: readonly ViewCopyItem[]): boolean =>
  copyWrites(items) > 0 || items.some((item) => item.written)

/**
 * "2 created, 1 updated, 5 unchanged, 1 skipped, 1 failed" of a copy, "To create 2 · To update 3 · Unchanged 5" of a
 * check: the counts that are not zero.
 */
export function copySummary(result: Pick<ViewCopyResult, 'items' | 'dryRun'>): string {
  const counts = copyCounts(result.items)
  const parts = result.dryRun
    ? ([
        ['To create', counts.Created],
        ['To update', counts.Updated],
        ['Unchanged', counts.Unchanged],
        ['To skip', counts.Skipped],
        ['Fails', counts.Failed],
      ] as const)
    : ([
        ['created', counts.Created],
        ['updated', counts.Updated],
        ['unchanged', counts.Unchanged],
        ['skipped', counts.Skipped],
        ['failed', counts.Failed],
      ] as const)
  const shown = parts.filter(([, count]) => count > 0)
  if (shown.length === 0) {
    return result.dryRun ? 'Nothing to copy' : 'nothing copied'
  }
  return result.dryRun
    ? shown.map(([label, count]) => `${label} ${count}`).join(' · ')
    : shown.map(([label, count]) => `${count} ${label}`).join(', ')
}
