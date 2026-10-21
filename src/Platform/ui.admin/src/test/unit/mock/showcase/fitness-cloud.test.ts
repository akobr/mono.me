import { describe, expect, it } from 'vitest'
import { combinedSchema, effectiveContent, schemaErrors } from '@/mock/configuration-handlers'
import { showcaseScope } from '@/mock/showcase'
import { fitnessCloud } from '@/mock/showcase/projects/fitness-cloud'
import { getAllAncestors } from '@/utils/annotation-key'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// fabrikam/fitness-cloud follows example 2 of the Miro board: a multi-tenant B2B SaaS whose tenants (gym chains)
// enable its applications and assign them to their members. Units and units of execution are not used.

const showcase = showcaseOf('fabrikam', 'fitness-cloud')
const views = fitnessCloud.views.map((view) => view.name)
const ofType = (keys: Set<string>, code: string) => [...keys].filter((key) => key.startsWith(`${code}.`)).toSorted()
/** The calendar day of an ISO timestamp or date, as a day number. */
const dayOf = (date: string) => Math.floor(Date.parse(date.length === 10 ? `${date}T12:00:00Z` : date) / 86_400_000)
/**
 * The day of an annotation's date (Created, ExpiresAt follow the clock) on the calendar of the configurations, which
 * are written for 21 October 2026, so the two compare on any day the tests run.
 */
const calendarDay = (date: string) => dayOf(date) - Math.floor(Date.now() / 86_400_000) + dayOf('2026-10-21')
/** The assignments of one member: exe.<tenant>.<application>.<member>. */
const assignmentsOf = (keys: Set<string>, tenant: string, member: string) =>
  ofType(keys, 'exe').filter((key) => key.startsWith(`exe.${tenant}.`) && key.endsWith(`.${member}`))

const boardDefault = [
  'sbt.fitfactory',
  'usg.fitfactory.class-booking',
  'exe.fitfactory.class-booking.jnovak',
  'exe.fitfactory.class-booking.kschwarz',
  'usg.fitfactory.gym-access',
  'exe.fitfactory.gym-access.epetrova',
  'exe.fitfactory.gym-access.mdvorak',
  'usg.fitfactory.nutrition-coach',
  'exe.fitfactory.nutrition-coach.epetrova',
  'rst.class-booking',
  'rst.gym-access',
  'rst.nutrition-coach',
  'cnt.fitfactory.jnovak',
  'cnt.fitfactory.kschwarz',
  'cnt.fitfactory.mdvorak',
  'cnt.fitfactory.epetrova',
]

const boardStaging = [
  'sbt.fitfactory',
  'usg.fitfactory.gym-access',
  'exe.fitfactory.gym-access.test-member1',
  'usg.fitfactory.nutrition-coach',
  'exe.fitfactory.nutrition-coach.test-member1',
  'exe.fitfactory.nutrition-coach.test-member2',
  'rst.gym-access',
  'rst.nutrition-coach',
  'cnt.fitfactory.test-member1',
  'cnt.fitfactory.test-member2',
]

describe('fabrikam/fitness-cloud', () => {
  it('has the sixteen boxes of the board’s default view', () => {
    const keys = showcase.keys('default')
    for (const key of boardDefault) {
      expect(keys.has(key), key).toBe(true)
    }
    // epetrova has gym access and the nutrition coach; the other three members one application each.
    expect(assignmentsOf(keys, 'fitfactory', 'epetrova')).toEqual([
      'exe.fitfactory.gym-access.epetrova',
      'exe.fitfactory.nutrition-coach.epetrova',
    ])
    expect(assignmentsOf(keys, 'fitfactory', 'jnovak')).toEqual(['exe.fitfactory.class-booking.jnovak'])
    expect(assignmentsOf(keys, 'fitfactory', 'kschwarz')).toEqual(['exe.fitfactory.class-booking.kschwarz'])
    expect(assignmentsOf(keys, 'fitfactory', 'mdvorak')).toEqual(['exe.fitfactory.gym-access.mdvorak'])
  })

  it('models six tenants, ten applications and hundreds of members with their assignments', () => {
    const keys = showcase.keys('default')
    expect(ofType(keys, 'sbt')).toEqual([
      'sbt.aqua-arena',
      'sbt.family-fit',
      'sbt.fitfactory',
      'sbt.ironworks',
      'sbt.pulse-gyms',
      'sbt.yoga-loft',
    ])
    expect(ofType(keys, 'rst')).toEqual(
      [
        'body-scan',
        'class-booking',
        'gym-access',
        'kids-club',
        'locker-rental',
        'nutrition-coach',
        'online-classes',
        'personal-training',
        'sauna',
        'spa-access',
      ].map((name) => `rst.${name}`),
    )
    expect(ofType(keys, 'usg').length).toBeGreaterThanOrEqual(36)
    expect(ofType(keys, 'usg').length).toBeLessThanOrEqual(48)
    expect(ofType(keys, 'cnt').length).toBeGreaterThanOrEqual(500)
    expect(ofType(keys, 'cnt').length).toBeLessThanOrEqual(600)
    expect(ofType(keys, 'exe').length).toBeGreaterThanOrEqual(380)
    expect(ofType(keys, 'exe').length).toBeLessThanOrEqual(470)
    // Yoga Loft has no gym floor, so no gym access.
    expect(keys.has('usg.yoga-loft.gym-access')).toBe(false)
  })

  it('uses neither units nor units of execution, and names only the types it uses', () => {
    for (const view of views) {
      const keys = showcase.keys(view)
      expect(ofType(keys, 'unt'), view).toEqual([])
      expect(ofType(keys, 'uxe'), view).toEqual([])
    }
    expect(Object.keys(fitnessCloud.settings.types).toSorted()).toEqual(['cnt', 'exe', 'rst', 'sbt', 'usg'])
    expect(fitnessCloud.settings.types).toMatchObject({
      rst: { label: 'Application' },
      sbt: { label: 'Tenant' },
      usg: { label: 'App enablement' },
      cnt: { label: 'Member' },
      exe: { label: 'App assignment' },
    })
  })

  it('names members by username, with the full name as title and the e-mail and member-since as values', () => {
    const annotations = showcase.annotations('default')
    expect(annotations.get('cnt.fitfactory.mdvorak')).toMatchObject({
      Title: 'Martin Dvořák',
      Values: { email: 'mdvorak@schranka.example', memberSince: '2019-09' },
    })
    expect(annotations.get('cnt.fitfactory.epetrova')?.Labels).toEqual(['plus', 'student', 'club-dejvice'])

    const members = [...annotations.values()].filter((annotation) => annotation.AnnotationKey.startsWith('cnt.'))
    for (const member of members) {
      const key = member.AnnotationKey
      expect(member.Name, key).toMatch(/^[a-z]+$/)
      expect(member.Title, key).toMatch(/^\S+ \S+$/)
      expect(member.Values?.email, key).toMatch(new RegExp(`^${member.Name}@[a-z-]+\\.example$`))
      expect(member.Values?.memberSince, key).toMatch(/^\d{4}-\d{2}$/)
      expect(
        member.Labels?.filter((label) => ['basic', 'plus', 'premium'].includes(label)),
        key,
      ).toHaveLength(1)
      expect(
        member.Labels?.some((label) => label.startsWith('club-')),
        key,
      ).toBe(true)
    }
    // Usernames are per tenant (the key holds the tenant): the board's kschwarz has a namesake at another chain.
    // Within a tenant, a clash gets a letter, never a number (the name pattern above).
    const namesakes = members.filter((member) => member.Name === 'kschwarz').map((member) => member.AnnotationKey)
    expect(namesakes).toContain('cnt.fitfactory.kschwarz')
    expect(namesakes.length).toBeGreaterThan(1)
    // Every assignment's plan is one of its application's plans (the descendant-type schemas).
    expect(showcase.own('default', 'exe.fitfactory.gym-access.mdvorak')).toMatchObject({ plan: 'all-clubs' })
    expect(showcase.latest('default', 'schema:descendant:rst.gym-access:exe')).toMatchObject({
      properties: { plan: { enum: ['off-peak', 'full', 'all-clubs'] } },
    })
  })

  it('keeps the tenant’s facts in values and the prices in the app enablements', () => {
    expect(showcase.annotations('default').get('sbt.fitfactory')?.Values).toMatchObject({
      legalName: 'FitFactory s.r.o.',
      country: 'CZ',
      currency: 'CZK',
      supportTier: 'premium',
    })
    for (const key of ofType(showcase.keys('default'), 'usg')) {
      expect(Object.keys(showcase.own('default', key) ?? {}), key).toEqual(
        expect.arrayContaining(['price', 'currency', 'billingPeriod', 'trialDays']),
      )
    }
    expect(showcase.latest('default', 'schema:type:usg')).toMatchObject({ required: ['price', 'currency'] })
    expect(showcase.latest('default', 'schema:type:exe')).toMatchObject({
      required: ['plan'],
      properties: { plan: { enum: expect.arrayContaining(['unlimited', 'all-clubs', 'coached']) } },
    })
  })

  it('keeps FitFactory’s gym access price list in sixteen versions', () => {
    expect(showcase.versions('default', 'usg.fitfactory.gym-access')).toBe(16)
    expect(showcase.own('default', 'usg.fitfactory.gym-access')).toEqual({
      price: 690,
      currency: '@annotation("/currency", "Subject")',
      billingPeriod: 'month',
      trialDays: 7,
      planPrices: { 'off-peak': 520, full: 690, 'all-clubs': 950 },
      joiningFee: 0,
      annualPrepayDiscountPercent: 12,
      offPeakHours: { weekdays: '06:00-16:00', weekends: 'all-day' },
    })
  })

  it('rejects a plan the application does not sell and a price list without a price', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    const assignment = combinedSchema(db, scope, 'exe.fitfactory.gym-access.mdvorak')?.MergedContent
    expect(schemaErrors(assignment, { plan: 'all-clubs', startDate: '2024-05-06' })).toEqual([])
    // Unlimited is a class-booking plan: gym access replaces the plans of the type schema with its own.
    expect(schemaErrors(assignment, { plan: 'unlimited' })).toContain('#/plan: NotInEnumeration')
    expect(schemaErrors(assignment, { startDate: '2024-05-06' })).toContain('#/plan: PropertyRequired')
    const enablement = combinedSchema(db, scope, 'usg.fitfactory.gym-access')?.MergedContent
    expect(schemaErrors(enablement, { currency: 'CZK', billingPeriod: 'month' })).toContain('#/price: PropertyRequired')
  })

  it('starts every assignment after its member, tenant and application exist, and before the membership ended', () => {
    for (const view of views) {
      const { annotations, trash, documents } = showcaseScope('fabrikam', 'fitness-cloud', view)!
      const all = new Map(
        [...annotations, ...trash.flatMap((tree) => tree.annotations)].map((item) => [item.AnnotationKey, item]),
      )
      for (const [key, annotation] of all) {
        const created = dayOf(annotation.Created!.At!)
        const versions = documents.get(`configuration:${key}`)?.versions ?? []
        if (versions.length) {
          expect(dayOf(versions[0]!.Modified!.At!), `${view} ${key}`).toBeGreaterThanOrEqual(created)
        }
        const firstStart = versions[0]?.Content?.startDate as string | undefined
        for (const ancestor of getAllAncestors(key).map((ancestorKey) => all.get(ancestorKey)!)) {
          expect(dayOf(ancestor.Created!.At!), `${view} ${key}`).toBeLessThanOrEqual(created)
          if (firstStart) {
            expect(calendarDay(ancestor.Created!.At!), `${view} ${key} starts ${firstStart}`).toBeLessThanOrEqual(
              dayOf(firstStart),
            )
            if (ancestor.ExpiresAt) {
              expect(dayOf(firstStart), `${view} ${key}`).toBeLessThanOrEqual(calendarDay(ancestor.ExpiresAt))
            }
          }
        }
      }
    }
    // The Yoga Loft spa ran its three-month trial.
    const trial = showcase.annotations('default').get('usg.yoga-loft.spa-access')!
    expect(dayOf(trial.ExpiresAt!) - dayOf(trial.Created!.At!)).toBe(92)
  })

  it('gives every assignment the renewal defaults, the member’s e-mail and the tenant’s support address', () => {
    expect(showcase.latest('default', 'template:exe')).toEqual({
      autoRenew: true,
      gracePeriodDays: 7,
      notifications: {
        email: '@annotation("/email", "Context")',
        replyTo: '@config("/supportEmail")',
        channels: ['email', 'push'],
        renewalReminderDays: 3,
      },
    })
    const key = 'exe.fitfactory.gym-access.epetrova'
    expect(effectiveContent(unseededShowcaseDb(), showcase.scope('default'), key)).toMatchObject({
      plan: 'full',
      discount: { percent: 20, reason: 'student' },
      autoRenew: true,
      supportEmail: 'support@fitfactory.example',
      // The member's own preference wins over the tenant's locale.
      locale: 'sk-SK',
    })
    // The bindings read values every member and every tenant has.
    for (const annotation of showcase.annotations('default').values()) {
      if (annotation.AnnotationKey.startsWith('cnt.')) {
        expect(annotation.Values?.email, annotation.AnnotationKey).toBeDefined()
      }
      if (annotation.AnnotationKey.startsWith('sbt.')) {
        expect(annotation.Values?.currency, annotation.AnnotationKey).toMatch(/^[A-Z]{3}$/)
      }
    }
  })

  it('shows scheduled, expired, frozen and erased memberships', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)

    // Bought ahead: Zuzana's spa membership starts next month, and the Family Fit sauna opens on 15 November.
    expect(status('exe.aqua-arena.spa-access.zhorvathova')).toBe('scheduled')
    expect(showcase.own('default', 'exe.aqua-arena.spa-access.zhorvathova')).toMatchObject({ startDate: '2026-11-01' })
    expect(status('usg.family-fit.sauna')).toBe('scheduled')
    const scheduled = ofType(showcase.keys('default'), 'exe').filter((key) => status(key) === 'scheduled')
    expect(scheduled.length).toBeGreaterThan(5)
    for (const key of scheduled) {
      expect(['2026-11-01', '2026-11-15'], key).toContain(showcase.own('default', key)?.startDate)
    }

    // Chloe cancelled; her membership and assignments expired.
    expect(status('cnt.yoga-loft.cwright')).toBe('expired')
    expect(assignmentsOf(showcase.keys('default'), 'yoga-loft', 'cwright').map(status)).toEqual(['expired', 'expired'])
    expect(status('usg.yoga-loft.spa-access')).toBe('expired')

    // Petr's membership is frozen; the kids club of Aqua Arena closes at the end of the year.
    expect(status('exe.fitfactory.gym-access.pkucera')).toBe('disabled')
    expect(status('usg.aqua-arena.kids-club')).toBe('active')
    expect(annotations.get('usg.aqua-arena.kids-club')?.ExpiresAt).toBeDefined()

    // Lena asked for the erasure of her data; a locker booked twice was removed alone.
    expect(showcase.trash('default').get('cnt.pulse-gyms.lbecker')?.toSorted()).toEqual([
      'cnt.pulse-gyms.lbecker',
      'exe.pulse-gyms.class-booking.lbecker',
      'exe.pulse-gyms.gym-access.lbecker',
    ])
    expect(showcase.trash('default').get('exe.fitfactory.locker-rental.tsvobodova')).toEqual([
      'exe.fitfactory.locker-rental.tsvobodova',
    ])
    expect(showcase.keys('default').has('cnt.pulse-gyms.lbecker')).toBe(false)
  })

  it('follows the board in staging: FitFactory, gym access and the nutrition coach, test members only', () => {
    const keys = showcase.keys('staging')
    for (const key of boardStaging) {
      expect(keys.has(key), key).toBe(true)
    }
    expect(ofType(keys, 'sbt')).toEqual(['sbt.fitfactory'])
    expect(ofType(keys, 'rst')).toEqual(['rst.gym-access', 'rst.nutrition-coach'])
    expect([...keys].some((key) => key.includes('class-booking'))).toBe(false)
    expect(ofType(keys, 'cnt').every((key) => key.startsWith('cnt.fitfactory.test-'))).toBe(true)
    expect(assignmentsOf(keys, 'fitfactory', 'test-member1')).toEqual([
      'exe.fitfactory.gym-access.test-member1',
      'exe.fitfactory.nutrition-coach.test-member1',
    ])
    expect(assignmentsOf(keys, 'fitfactory', 'test-member2')).toEqual(['exe.fitfactory.nutrition-coach.test-member2'])
    expect(showcase.own('staging', 'sbt.fitfactory')).toMatchObject({
      appHost: 'fitfactory.staging.fitness-cloud.fabrikam.example',
      payments: { environment: 'test', webhookSecret: '@(fitfactory.adyen-webhook-staging, primaryVault)' },
    })
  })

  it('develops on one sandbox tenant with five test members and debug logging', () => {
    const keys = showcase.keys('development')
    expect(ofType(keys, 'sbt')).toEqual(['sbt.sandbox-gym'])
    expect(ofType(keys, 'cnt')).toHaveLength(5)
    expect(keys.has('cnt.sandbox-gym.turnstile-rig')).toBe(true)
    expect(showcase.own('development', 'sbt.sandbox-gym')).toMatchObject({ logLevel: 'debug' })
    expect(showcase.own('development', 'rst.gym-access')).toMatchObject({ logLevel: 'debug', simulateHardware: true })
  })

  it('brings the 2027 prices of two tenants, a stricter price schema and the challenges in the release candidate', () => {
    const keys = showcase.keys('release-candidate')
    expect(ofType(keys, 'sbt')).toEqual(['sbt.fitfactory', 'sbt.pulse-gyms'])
    expect(showcase.versions('release-candidate', 'usg.fitfactory.gym-access')).toBe(17)
    const price = (view: string, key: string) => showcase.own(view, key)?.price as number
    expect(price('release-candidate', 'usg.fitfactory.gym-access')).toBeGreaterThan(
      price('default', 'usg.fitfactory.gym-access'),
    )
    expect(price('release-candidate', 'usg.pulse-gyms.class-booking')).toBeGreaterThan(
      price('default', 'usg.pulse-gyms.class-booking'),
    )
    // The 2027 prices are valid from 1 January.
    expect(showcase.own('release-candidate', 'usg.pulse-gyms.class-booking')).toMatchObject({ validFrom: '2027-01-01' })
    expect(showcase.latest('release-candidate', 'schema:type:usg')).toMatchObject({
      required: ['price', 'currency', 'billingPeriod'],
    })
    expect(showcase.own('release-candidate', 'rst.class-booking')).toMatchObject({ smartWaitlist: true })
    expect(keys.has('usg.pulse-gyms.challenges')).toBe(true)
    expect(showcase.keys('default').has('rst.challenges')).toBe(false)
    // The release candidate copies production without its deletions.
    expect(keys.has('cnt.fitfactory.epetrova')).toBe(true)
    expect(keys.has('cnt.pulse-gyms.lbecker')).toBe(false)
  })

  it('delivers the AI coach beta to two FitFactory members in feature-delivery1', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(ofType(keys, 'sbt')).toEqual(['sbt.fitfactory'])
    expect(keys.has('rst.ai-coach')).toBe(true)
    expect(ofType(keys, 'exe').filter((key) => key.startsWith('exe.fitfactory.ai-coach.'))).toEqual([
      'exe.fitfactory.ai-coach.epetrova',
      'exe.fitfactory.ai-coach.jnovak',
    ])
    expect(showcase.own('feature-delivery1', 'exe.fitfactory.ai-coach.jnovak')).toMatchObject({ plan: 'beta' })
    expect(showcase.keys('default').has('rst.ai-coach')).toBe(false)
  })
})
