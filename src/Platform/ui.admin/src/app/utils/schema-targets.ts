import {
  deleteAnnotationSchema,
  deleteConfigurationSchema,
  deleteDescendantTypeSchema,
  getAnnotationSchema,
  getAnnotationSchemaVersion,
  getAnnotationSchemaVersionDiffCustom,
  getAnnotationSchemaVersions,
  getConfigurationSchema,
  getDescendantTypeSchema,
  getDescendantTypeSchemaVersion,
  getDescendantTypeSchemaVersionDiffCustom,
  getDescendantTypeSchemaVersions,
  getSchemaVersion,
  getSchemaVersionDiffCustom,
  getSchemaVersions,
  setAnnotationSchema,
  setConfigurationSchema,
  setDescendantTypeSchema,
  type ConfigurationSchema,
  type ConfigurationSchemaSummary,
} from '@42for.net/2splatform.sdk'
import type { VersionSource } from '@/components/versions/version-source'
import type { Scope } from '@/stores/context'
import { metaOf, type AnnotationTypeCode } from './annotation-key'

/** The three schema kinds (spec section 14), identified like the API: a type code and/or an annotation key. */
export type SchemaTarget =
  | { kind: 'type'; type: AnnotationTypeCode }
  | { kind: 'annotation'; key: string }
  | { kind: 'descendant'; key: string; type: AnnotationTypeCode }

export interface SchemaApi {
  target: SchemaTarget
  title: string
  description: string
  /** Path segments of the resource, the base of every query key (path-prefix invalidation). */
  queryKey: readonly string[]
  path: string
  get: (signal?: AbortSignal) => Promise<ConfigurationSchema>
  set: (content: Record<string, unknown>, force: boolean) => Promise<ConfigurationSchema>
  remove: () => Promise<void>
  versions: Omit<VersionSource, 'current'>
  /** Does a configuration of this key fall under the schema? */
  appliesTo: (annotationKey: string, ancestors: readonly string[]) => boolean
}

const label = (type: string) => metaOf(type)?.label ?? type

export function schemaApi(scope: Scope, target: SchemaTarget): SchemaApi {
  const { org, project, view } = scope
  const base = `/v1/${org}/${project}/${view}/configuration-schema`

  switch (target.kind) {
    case 'type': {
      const path = `${base}/type/${target.type}`
      return {
        target,
        title: `${label(target.type)} schema`,
        description: `Validates every ${label(target.type).toLowerCase()} configuration of the view.`,
        queryKey: path.split('/').filter(Boolean),
        path,
        get: (signal) => getConfigurationSchema(org, project, view, target.type, { signal }),
        set: (content, force) =>
          setConfigurationSchema(org, project, view, target.type, content, force ? { force } : undefined),
        remove: () => deleteConfigurationSchema(org, project, view, target.type),
        versions: {
          queryKey: path.split('/').filter(Boolean),
          list: (signal) => getSchemaVersions(org, project, view, target.type, { signal }),
          content: async (version, signal) =>
            (await getSchemaVersion(org, project, view, target.type, version, { signal })).Content,
          diff: (version, from, signal) =>
            getSchemaVersionDiffCustom(org, project, view, target.type, version, from, undefined, { signal }),
        },
        appliesTo: (key) => metaOf(key.slice(0, 3))?.code === target.type,
      }
    }
    case 'annotation': {
      const path = `${base}/${target.key}`
      return {
        target,
        title: `Schema of ${target.key}`,
        description: 'Validates the configuration of this annotation, on top of its type schema.',
        queryKey: path.split('/').filter(Boolean),
        path,
        get: (signal) => getAnnotationSchema(org, project, view, target.key, { signal }),
        set: (content, force) =>
          setAnnotationSchema(org, project, view, target.key, content, force ? { force } : undefined),
        remove: () => deleteAnnotationSchema(org, project, view, target.key),
        versions: {
          queryKey: [...path.split('/').filter(Boolean), 'own'],
          list: (signal) => getAnnotationSchemaVersions(org, project, view, target.key, { signal }),
          content: async (version, signal) =>
            (await getAnnotationSchemaVersion(org, project, view, target.key, version, { signal })).Content,
          diff: (version, from, signal) =>
            getAnnotationSchemaVersionDiffCustom(org, project, view, target.key, version, from, undefined, { signal }),
        },
        appliesTo: (key) => key === target.key,
      }
    }
    case 'descendant': {
      const path = `${base}/${target.key}/type/${target.type}`
      return {
        target,
        title: `${label(target.type)} schema below ${target.key}`,
        description: `Validates the ${label(target.type).toLowerCase()} configurations that descend from ${target.key}.`,
        queryKey: path.split('/').filter(Boolean),
        path,
        get: (signal) => getDescendantTypeSchema(org, project, view, target.key, target.type, { signal }),
        set: (content, force) =>
          setDescendantTypeSchema(org, project, view, target.key, target.type, content, force ? { force } : undefined),
        remove: () => deleteDescendantTypeSchema(org, project, view, target.key, target.type),
        versions: {
          queryKey: path.split('/').filter(Boolean),
          list: (signal) => getDescendantTypeSchemaVersions(org, project, view, target.key, target.type, { signal }),
          content: async (version, signal) =>
            (await getDescendantTypeSchemaVersion(org, project, view, target.key, target.type, version, { signal }))
              .Content,
          diff: (version, from, signal) =>
            getDescendantTypeSchemaVersionDiffCustom(
              org,
              project,
              view,
              target.key,
              target.type,
              version,
              from,
              undefined,
              { signal },
            ),
        },
        appliesTo: (key, ancestors) => metaOf(key.slice(0, 3))?.code === target.type && ancestors.includes(target.key),
      }
    }
  }
}

/** The route of a schema in the schemas section. */
export function schemaRoute(scope: Scope, target: SchemaTarget): string {
  const base = `/${scope.org}/${scope.project}/${scope.view}/schemas`
  switch (target.kind) {
    case 'type':
      return `${base}/type/${target.type}`
    case 'annotation':
      return `${base}/annotation/${target.key}`
    case 'descendant':
      return `${base}/annotation/${target.key}/type/${target.type}`
  }
}

/** A schema listing row (companion spec E7) as a target. */
export function summaryTarget(summary: ConfigurationSchemaSummary): SchemaTarget | undefined {
  const type = metaOf(summary.AnnotationType)?.code
  if (summary.Kind === 'Type' && type) {
    return { kind: 'type', type }
  }
  if (summary.Kind === 'Annotation' && summary.AnnotationKey) {
    return { kind: 'annotation', key: summary.AnnotationKey }
  }
  if (summary.Kind === 'DescendantType' && summary.AnnotationKey && type) {
    return { kind: 'descendant', key: summary.AnnotationKey, type }
  }
  return undefined
}

/** A starting point for a new schema. */
export const starterSchema = {
  $schema: 'http://json-schema.org/draft-07/schema#',
  type: 'object',
  properties: {},
}
