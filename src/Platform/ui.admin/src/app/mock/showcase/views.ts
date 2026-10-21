import type { ShowcaseView } from './types'

/** The environments every showcase project registers, in this order (424-77); `default` is production. */
export const environmentViewNames = [
  'default',
  'development',
  'staging',
  'release-candidate',
  'feature-delivery1',
] as const

export type EnvironmentView = (typeof environmentViewNames)[number]

const descriptions: Record<EnvironmentView, string> = {
  default: 'Production environment',
  development: 'Development environment',
  staging: 'Staging environment',
  'release-candidate': 'Release candidate',
  'feature-delivery1': 'Feature delivery 1',
}

/** The five environment views with the standard descriptions, a project's refinements, then its own extra views. */
export function environmentViews(
  refined: Partial<Record<Exclude<EnvironmentView, 'default'>, string>> = {},
  extra: ShowcaseView[] = [],
): ShowcaseView[] {
  return [
    ...environmentViewNames.map((name) => ({
      name,
      description: name === 'default' ? descriptions.default : (refined[name] ?? descriptions[name]),
    })),
    ...extra,
  ]
}
