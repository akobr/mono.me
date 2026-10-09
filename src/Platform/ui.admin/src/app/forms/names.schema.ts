import { z } from 'zod'

// Mirrors Backend.Core NameRules (companion spec E11); the API validates again.
export const namePattern = /^[a-z0-9][a-z0-9-]{1,62}$/

export const reservedOrganizationNames: ReadonlySet<string> = new Set([
  'access',
  'auth',
  'login',
  'callback',
  'onboarding',
  'invitations',
  'account',
  'orgs',
  'unsupported',
])

export const reservedProjectAndViewNames: ReadonlySet<string> = new Set([
  'access',
  'views',
  'members',
  'invitations',
  'machines',
  'certificates',
  'settings',
])

const nameSchema = (kind: string, reserved: ReadonlySet<string>) =>
  z
    .string()
    .trim()
    .min(1, `Enter a ${kind} name.`)
    .regex(namePattern, 'Use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit.')
    .refine((name) => !reserved.has(name), { message: `This ${kind} name is reserved.` })

export const organizationNameSchema = nameSchema('organization', reservedOrganizationNames)
export const projectNameSchema = nameSchema('project', reservedProjectAndViewNames)
export const viewNameSchema = nameSchema('view', reservedProjectAndViewNames)

/** Onboarding and "New organization": the API creates an organization through its first project. */
export const organizationSchema = z.object({
  organization: organizationNameSchema,
  project: projectNameSchema,
})

export const projectSchema = z.object({
  project: projectNameSchema,
})

export type OrganizationForm = z.infer<typeof organizationSchema>
export type ProjectForm = z.infer<typeof projectSchema>
