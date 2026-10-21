import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import { getResolvedConfiguration } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { combinedSchema, effectiveContent, ownContent, schemaErrors } from '@/mock/configuration-handlers'
import { saveDb } from '@/mock/db'
import { showcaseScope } from '@/mock/showcase'
import { streamingPlatform } from '@/mock/showcase/projects/streaming-platform'
import { annotationStatus } from '@/utils/annotation-status'
import { showcaseOf, unseededShowcaseDb } from './support'

// streamwave/streaming-platform follows docs/42for.net/platform/examples/distributed-platform.md and example 5 of the
// Miro board.

const showcase = showcaseOf('streamwave', 'streaming-platform')
const startingWith = (keys: Set<string>, prefix: string) => [...keys].filter((key) => key.startsWith(prefix))
/** When the latest version of a document was saved. */
const savedAt = (view: string, id: string) =>
  Date.parse(
    showcaseScope('streamwave', 'streaming-platform', view)
      ?.documents.get(id as never)
      ?.versions.at(-1)?.Modified.At ?? '',
  )

/** The keys of the board's release-candidate frame. */
const releaseCandidateFrame = [
  'sbt.eu',
  'rst.playback',
  'rst.recommendations',
  'cnt.eu.canary',
  'unt.recommendations.model-refresh',
  'usg.eu.playback',
  'usg.eu.recommendations',
  'exe.eu.playback.canary',
  'exe.eu.recommendations.canary',
  'uxe.eu.recommendations.canary.model-refresh',
]

describe('streamwave/streaming-platform', () => {
  it('names the types as the board does', () => {
    expect(
      Object.fromEntries(Object.entries(streamingPlatform.settings.types).map(([code, type]) => [code, type.label])),
    ).toEqual({
      rst: 'Service',
      unt: 'Pipeline',
      sbt: 'Market',
      usg: 'License',
      cnt: 'Cell',
      exe: 'Deployment',
      uxe: 'PipelineRun',
    })
  })

  it('has the catalog of the documentation and of the board', () => {
    const keys = showcase.keys('default')
    for (const key of [
      'sbt.eu',
      'sbt.us',
      'cnt.eu.prod',
      'cnt.eu.canary',
      'rst.playback',
      'rst.recommendations',
      'rst.search',
      'rst.billing',
      'unt.recommendations.model-refresh',
      'usg.eu.playback',
      'usg.eu.recommendations',
      'usg.eu.search',
      'usg.us.playback',
      'exe.eu.playback.prod',
      'exe.eu.playback.canary',
      'exe.eu.recommendations.prod',
      'exe.eu.recommendations.canary',
      'exe.eu.search.prod',
      'uxe.eu.recommendations.prod.model-refresh',
      'uxe.eu.recommendations.canary.model-refresh',
    ]) {
      expect(keys.has(key), key).toBe(true)
    }
    // Europe has a production and a canary cell; search meets production as shadow traffic and has no canary.
    expect(startingWith(keys, 'cnt.eu.')).toEqual(['cnt.eu.prod', 'cnt.eu.canary'])
    expect(keys.has('exe.eu.search.canary')).toBe(false)
  })

  it('has billing as a service that Europe does not run yet', () => {
    const keys = showcase.keys('default')
    expect(keys.has('rst.billing')).toBe(true)
    expect([...keys].filter((key) => /^(usg|exe|uxe)\.eu\.billing(\.|$)/.test(key))).toEqual([])
    expect(keys.has('usg.us.billing')).toBe(true)
  })

  it('stores the documents of the documentation’s table', () => {
    expect(showcase.own('default', 'rst.playback')).toMatchObject({
      codecLadder: { h264: [240, 360, 480, 720, 1080], hevc: [720, 1080, 1440, 2160] },
      startupTimeoutMs: 4500,
      featureFlags: ['av1-ladder', 'spatial-audio'],
    })
    expect(showcase.versions('default', 'rst.playback')).toBe(16)
    expect(showcase.own('default', 'sbt.eu')).toEqual({
      defaultAudioLanguage: 'en',
      catalogWindow: { territories: ['be', 'es', 'fr', 'ie', 'it', 'lu', 'nl', 'pt'], availabilityLagDays: 0 },
      storeCurrency: 'EUR',
    })
    // The store currency is the one of the market's legal entity; the territories that pay in another say so.
    expect(showcase.own('default', 'sbt.nordics')).toMatchObject({
      storeCurrency: 'SEK',
      territoryCurrencies: { dk: 'DKK', fi: 'EUR', no: 'NOK' },
    })
    expect(showcase.own('default', 'sbt.latam')).toMatchObject({
      defaultAudioLanguage: 'es',
      territoryAudioLanguages: { br: 'pt' },
      storeCurrency: 'BRL',
    })
    expect(showcase.own('default', 'usg.eu.playback')).toMatchObject({
      package: 'svod',
      contentTiers: ['standard', 'premium'],
    })
    expect(showcase.own('default', 'cnt.eu.canary')).toEqual({
      trafficShare: 0.05,
      tracingSampleRate: 0.25,
      buildChannel: 'canary',
      cloudRegion: 'eu-west-1',
    })
    expect(showcase.own('default', 'exe.eu.playback.canary')).toMatchObject({ flags: { 'av1-ladder': true } })
  })

  it('has the AV1 ladder on in the European canary and off in production', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    expect(effectiveContent(db, scope, 'exe.eu.playback.canary')).toMatchObject({
      flags: { 'av1-ladder': true },
      trafficShare: 0.05,
      buildChannel: 'canary',
    })
    // Production inherits the template: the rollout defaults and every flag off.
    expect(effectiveContent(db, scope, 'exe.eu.playback.prod')).toMatchObject({
      flags: { 'av1-ladder': false },
      rollout: { strategy: 'rolling', autoRollback: true },
      trafficShare: 0.95,
    })
    expect(showcase.own('default', 'exe.eu.playback.prod')?.flags).toBeUndefined()
  })

  it('tells a market-wide, a cell-wide and a single flag apart', () => {
    const db = unseededShowcaseDb()
    const scope = showcase.scope('default')
    expect(showcase.own('default', 'sbt.uk')).toMatchObject({ flags: { 'age-verification': true } })
    expect(effectiveContent(db, scope, 'exe.uk.playback.prod')).toMatchObject({ flags: { 'age-verification': true } })
    expect(showcase.own('default', 'cnt.us.canary')).toMatchObject({ flags: { 'http3-edge': true } })
    expect(effectiveContent(db, scope, 'exe.us.catalog.canary')).toMatchObject({
      flags: { 'http3-edge': true, 'graphql-gateway': true },
    })
    expect(effectiveContent(db, scope, 'exe.us.catalog.prod').flags).not.toHaveProperty('http3-edge')
  })

  it('refreshes the model in the canary cells before production, in the market’s time zone', () => {
    const annotations = showcase.annotations('default')
    expect(annotations.get('unt.recommendations.model-refresh')).toMatchObject({
      UnitType: 'time',
      UnitDefinition: '0 4 * * *',
    })
    expect(showcase.own('default', 'uxe.eu.recommendations.canary.model-refresh')).toEqual({ cron: '0 2 * * *' })
    // Production keeps the pipeline's schedule.
    expect(showcase.own('default', 'uxe.eu.recommendations.prod.model-refresh')).toBeUndefined()
    expect(annotations.get('uxe.eu.recommendations.canary.model-refresh')?.TimeZone).toBe('Europe/Amsterdam')
    expect(annotations.get('uxe.us.recommendations.canary.model-refresh')?.TimeZone).toBe('America/New_York')
    const canaryRuns = startingWith(showcase.keys('default'), 'uxe.').filter((key) =>
      key.endsWith('.canary.model-refresh'),
    )
    expect(canaryRuns).toHaveLength(7)
    expect(canaryRuns.every((key) => showcase.own('default', key)?.cron === '0 2 * * *')).toBe(true)
  })

  it('labels the services edge, ml or money and links their runbooks', () => {
    const annotations = showcase.annotations('default')
    expect(annotations.get('rst.playback')?.Labels).toContain('edge')
    expect(annotations.get('rst.recommendations')?.Labels).toContain('ml')
    expect(annotations.get('rst.billing')?.Labels).toContain('money')
    const services = startingWith(showcase.keys('default'), 'rst.')
    expect(services.length).toBeGreaterThanOrEqual(20)
    for (const key of services) {
      expect(annotations.get(key)?.DocumentationLink, key).toBe(
        `https://runbooks.streamwave.example/services/${key.slice(4)}`,
      )
    }
  })

  it('runs eight markets in their own time zones, with a second production cell in the big ones', () => {
    const keys = showcase.keys('default')
    const markets = startingWith(keys, 'sbt.')
    expect(markets).toEqual([
      'sbt.eu',
      'sbt.us',
      'sbt.uk',
      'sbt.nordics',
      'sbt.dach',
      'sbt.latam',
      'sbt.apac',
      'sbt.mena',
    ])
    const annotations = showcase.annotations('default')
    expect(new Set(markets.map((key) => annotations.get(key)?.TimeZone)).size).toBe(8)
    expect(startingWith(keys, 'cnt.').filter((key) => key.endsWith('.prod-2'))).toEqual([
      'cnt.us.prod-2',
      'cnt.apac.prod-2',
    ])
    for (const market of markets.map((key) => key.slice(4))) {
      expect(keys.has(`cnt.${market}.prod`) && keys.has(`cnt.${market}.canary`), market).toBe(true)
    }
    for (const pipeline of ['search.index-rebuild', 'catalog.ingest', 'ads.forecast']) {
      expect(keys.has(`unt.${pipeline}`), pipeline).toBe(true)
    }
  })

  it('types the rollout flags, limits the traffic share and shows one deployment as non-compliant', () => {
    expect(showcase.latest('default', 'schema:type:exe')).toMatchObject({
      properties: { flags: { additionalProperties: { type: 'boolean' } }, trafficShare: { minimum: 0, maximum: 1 } },
    })
    expect(streamingPlatform.nonCompliant).toEqual(['exe.us.ads.canary'])
    const db = unseededShowcaseDb()
    const errors = (view: string) => {
      const scope = showcase.scope(view)
      return schemaErrors(
        combinedSchema(db, scope, 'exe.us.ads.canary')?.MergedContent,
        ownContent(db, scope, 'exe.us.ads.canary'),
      )
    }
    // 20 was meant as 20 %; the release of playback 14 stores the fraction.
    expect(errors('default')).toEqual(['#/trafficShare: NumberTooBig'])
    expect(errors('playback-14')).toEqual([])
    expect(showcase.own('playback-14', 'exe.us.ads.canary')).toMatchObject({ trafficShare: 0.2 })
    // The 20 was saved before the schema limited the share, so the save passed; the fix of playback 14 came after.
    expect(savedAt('default', 'configuration:exe.us.ads.canary')).toBeLessThan(savedAt('default', 'schema:type:exe'))
    expect(savedAt('playback-14', 'configuration:exe.us.ads.canary')).toBeGreaterThan(
      savedAt('playback-14', 'schema:type:exe'),
    )
  })

  it('binds billing to the market’s legal entity, downloads to the cell’s region and DRM keys to the vault', () => {
    const keys = showcase.keys('default')
    const annotations = showcase.annotations('default')
    const db = unseededShowcaseDb()
    const billing = startingWith(keys, 'exe.').filter((key) => key.split('.')[2] === 'billing')
    expect(billing.length).toBeGreaterThan(0)
    for (const key of billing) {
      expect(showcase.own('default', key)?.invoiceIssuer, key).toBe('@annotation("/legalEntity", "Subject")')
      expect(annotations.get(`sbt.${key.split('.')[1]}`)?.Values?.legalEntity, key).toEqual(expect.any(String))
    }
    expect(showcase.own('default', 'exe.eu.downloads.prod')).toEqual({
      storage: { bucket: 'streamwave-downloads-eu', region: '@config("/cloudRegion")' },
    })
    expect(effectiveContent(db, showcase.scope('default'), 'exe.eu.downloads.prod').cloudRegion).toBe('eu-west-1')
    expect(showcase.own('default', 'exe.us.drm.prod-2')).toMatchObject({
      signingKey: '@(drm.us-signing-key, primaryVault)',
    })
  })

  it('follows the board in the release candidate: the canary cells only, with playback and recommendations', () => {
    const keys = showcase.keys('release-candidate')
    const production = showcase.keys('default')
    for (const key of releaseCandidateFrame) {
      expect(keys.has(key), key).toBe(true)
    }
    // A subset of production that keeps every market's canary cell and nothing else.
    expect([...keys].every((key) => production.has(key))).toBe(true)
    expect(startingWith(keys, 'rst.')).toEqual(['rst.playback', 'rst.recommendations'])
    expect(startingWith(keys, 'unt.')).toEqual(['unt.recommendations.model-refresh'])
    expect(startingWith(keys, 'cnt.')).toHaveLength(8)
    expect(startingWith(keys, 'cnt.').every((key) => key.endsWith('.canary'))).toBe(true)
    expect(startingWith(keys, 'usg.eu.')).toEqual(['usg.eu.playback', 'usg.eu.recommendations'])
    for (const key of [
      'cnt.eu.prod',
      'rst.search',
      'rst.billing',
      'usg.eu.search',
      'exe.eu.playback.prod',
      'exe.eu.recommendations.prod',
      'exe.eu.search.prod',
      'uxe.eu.recommendations.prod.model-refresh',
    ]) {
      expect(keys.has(key), key).toBe(false)
    }
  })

  it('brings the next release’s flags, documents and a tighter schema to the release candidate', () => {
    expect(showcase.own('release-candidate', 'exe.eu.playback.canary')?.flags).toEqual({
      'av1-ladder': true,
      'low-latency-hls': true,
    })
    expect(showcase.own('release-candidate', 'exe.eu.recommendations.canary')?.flags).toEqual({ 'ml-ranking-v2': true })
    expect(showcase.own('release-candidate', 'rst.recommendations')).toMatchObject({ model: { version: 'v9' } })
    expect(showcase.own('release-candidate', 'rst.playback')?.featureFlags).toContain('low-latency-hls')
    expect(showcase.own('release-candidate', 'cnt.eu.canary')).toMatchObject({ buildChannel: 'candidate' })
    expect(showcase.latest('release-candidate', 'schema:type:exe')).toMatchObject({
      properties: { flags: { propertyNames: { enum: expect.arrayContaining(['low-latency-hls', 'av1-ladder']) } } },
    })
    expect(showcase.latest('default', 'schema:type:exe')).not.toHaveProperty('properties.flags.propertyNames')
  })

  it('tries the new codec ladder of playback 14 in the canary cells of a copy of production', () => {
    const keys = showcase.keys('playback-14')
    expect(keys).toEqual(showcase.keys('default'))
    const canaries = startingWith(keys, 'exe.').filter((key) => key.endsWith('.playback.canary'))
    expect(canaries).toHaveLength(8)
    for (const key of canaries) {
      expect(showcase.own('playback-14', key)?.codecLadder, key).toEqual({
        h264: [360, 540, 720, 1080],
        av1: [360, 540, 720, 1080, 1440, 2160],
      })
    }
    // The diff of Europe production between the views: what it inherits from the new service document, and two
    // values overridden only in that cell.
    expect(showcase.own('default', 'exe.eu.playback.prod')).toEqual({ replicas: 180 })
    expect(showcase.own('playback-14', 'exe.eu.playback.prod')).toEqual({ replicas: 200, abr: { minBufferSeconds: 6 } })
    const db = unseededShowcaseDb()
    const live = effectiveContent(db, showcase.scope('default'), 'exe.eu.playback.prod')
    const candidate = effectiveContent(db, showcase.scope('playback-14'), 'exe.eu.playback.prod')
    expect([live.startupTimeoutMs, candidate.startupTimeoutMs]).toEqual([4500, 4000])
    expect([live.abr, candidate.abr]).toEqual([
      { algorithm: 'hybrid', minBufferSeconds: 8 },
      { algorithm: 'hybrid-v2', minBufferSeconds: 6 },
    ])
    expect([live.replicas, candidate.replicas]).toEqual([180, 200])
    expect(candidate.codecLadder).not.toHaveProperty('av1')
  })

  it('shows a market that launches next quarter, a retired cell, a paused deployment and a rights contract end', () => {
    const annotations = showcase.annotations('default')
    const status = (key: string) => annotationStatus(annotations.get(key)!)
    const mena = [...annotations.keys()].filter((key) => key.split('.')[1] === 'mena')
    expect(mena.length).toBeGreaterThan(10)
    expect(mena.every((key) => status(key) === 'scheduled')).toBe(true)
    const retired = [...annotations.keys()].filter(
      (key) => key.split('.')[3] === 'prod-iad' || key === 'cnt.latam.prod-iad',
    )
    expect(retired).toHaveLength(4)
    expect(retired.every((key) => status(key) === 'expired')).toBe(true)
    // Drained and scaled to zero before it expired.
    for (const key of retired) {
      expect(savedAt('default', `configuration:${key}`), key).toBeLessThan(
        Date.parse(annotations.get(key)?.ExpiresAt ?? ''),
      )
    }
    expect(status('exe.latam.ads.prod')).toBe('disabled')
    expect(status('exe.latam.ads.canary')).toBe('disabled')
    expect(status('uxe.latam.ads.prod.forecast')).toBe('disabled')
    expect(status('usg.uk.live-events')).toBe('active')
    expect(annotations.get('usg.uk.live-events')?.ExpiresAt).toBeDefined()
  })

  it('starts with a deleted service and its pipelines, and a pipeline run deleted alone', () => {
    const trash = showcase.trash('default')
    expect(trash.get('rst.watch-parties')).toEqual(
      expect.arrayContaining([
        'unt.watch-parties.room-cleanup',
        'unt.watch-parties.invite-reminder',
        'usg.us.watch-parties',
        'exe.uk.watch-parties.prod',
        'uxe.us.watch-parties.prod.room-cleanup',
        'rst.watch-parties',
      ]),
    )
    expect(trash.get('rst.watch-parties')?.at(-1)).toBe('rst.watch-parties')
    expect(trash.get('uxe.apac.search.prod-2.index-rebuild')).toEqual(['uxe.apac.search.prod-2.index-rebuild'])
    expect(showcase.keys('default').has('exe.apac.search.prod-2')).toBe(true)
  })

  it('delivers clips to the Nordic canary cell in feature-delivery1', () => {
    const keys = showcase.keys('feature-delivery1')
    expect(startingWith(keys, 'sbt.')).toEqual(['sbt.nordics'])
    expect(startingWith(keys, 'rst.')).toEqual(['rst.playback', 'rst.catalog', 'rst.clips'])
    expect(keys.has('exe.nordics.clips.canary')).toBe(true)
    expect(keys.has('exe.nordics.clips.prod')).toBe(false)
    expect(keys.has('uxe.nordics.clips.canary.highlight-extract')).toBe(true)
    expect(showcase.keys('default').has('rst.clips')).toBe(false)
    // The new service brings its pipeline, license, deployment and run along.
    const annotations = showcase.annotations('feature-delivery1')
    const created = (key: string) => annotations.get(key)?.Created?.At
    for (const key of [
      'unt.clips.highlight-extract',
      'usg.nordics.clips',
      'exe.nordics.clips.canary',
      'uxe.nordics.clips.canary.highlight-extract',
    ]) {
      expect(created(key), key).toBe(created('rst.clips'))
    }
  })

  it('develops against sandboxes and tests in staging cells and a test territory', () => {
    const development = showcase.keys('development')
    expect(startingWith(development, 'cnt.')).toEqual(['cnt.eu.dev', 'cnt.eu.qa', 'cnt.eu.device-lab', 'cnt.us.dev'])
    expect(showcase.own('development', 'cnt.eu.dev')).toMatchObject({ logLevel: 'debug', tracingSampleRate: 1 })
    expect(showcase.own('development', 'exe.eu.drm.dev')?.licenseServer).toBe(
      'https://drm-sandbox.streamwave.example/widevine',
    )

    const staging = showcase.keys('staging')
    expect(startingWith(staging, 'sbt.')).toEqual(['sbt.eu', 'sbt.us', 'sbt.apac', 'sbt.zz'])
    expect(startingWith(staging, 'cnt.').every((key) => /\.(staging|load-test)$/.test(key))).toBe(true)
    expect(staging.has('usg.eu.billing')).toBe(false)
    expect(showcase.own('staging', 'exe.zz.billing.staging')).toMatchObject({ pspMode: 'test' })
    // Only the load-test cell runs production's replica counts.
    expect(showcase.own('staging', 'exe.us.playback.staging')?.replicas).toBe(4)
    expect(showcase.own('staging', 'exe.us.playback.load-test')?.replicas).toBe(288)
  })

  it('saves no configuration before its annotation was created', () => {
    for (const view of streamingPlatform.views.map((entry) => entry.name)) {
      const scope = showcaseScope('streamwave', 'streaming-platform', view)!
      const annotations = [...scope.annotations, ...scope.trash.flatMap((tree) => tree.annotations)]
      for (const annotation of annotations) {
        const first = scope.documents.get(`configuration:${annotation.AnnotationKey}`)?.versions[0]
        if (first) {
          expect(Date.parse(first.Modified.At ?? ''), `${view} ${annotation.AnnotationKey}`).toBeGreaterThanOrEqual(
            Date.parse(annotation.Created?.At ?? ''),
          )
        }
      }
    }
  })

  describe('through the mock API', () => {
    const server = setupServer(...mockHandlers)
    beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
    // The test setup clears the session after every test, so each test stores the project's database again.
    beforeEach(() =>
      saveDb({ ...unseededShowcaseDb(), account: { AccessMap: { 'streamwave.streaming-platform': 'Contributor' } } }),
    )
    afterEach(() => server.resetHandlers())
    afterAll(() => server.close())

    const resolved = async (view: string, key: string) =>
      (await getResolvedConfiguration('streamwave', 'streaming-platform', view, key)).Content

    it('resolves billing to the market’s legal entity and downloads to the cell’s region', async () => {
      expect(await resolved('default', 'exe.us.billing.prod')).toMatchObject({
        invoiceIssuer: 'Streamwave Inc.',
        // Secrets need the vault, so the mock returns them as written.
        pspWebhookSecret: '@(billing.us-psp-webhook, primaryVault)',
      })
      expect(await resolved('staging', 'exe.zz.billing.staging')).toMatchObject({
        invoiceIssuer: 'Streamwave International B.V.',
      })
      expect(await resolved('default', 'exe.eu.downloads.prod')).toMatchObject({
        storage: { bucket: 'streamwave-downloads-eu', region: 'eu-west-1' },
      })
      expect(await resolved('default', 'exe.us.drm.prod-2')).toMatchObject({
        signingKey: '@(drm.us-signing-key, primaryVault)',
      })
    })

    it('resolves every configuration of every view', async () => {
      for (const view of streamingPlatform.views.map((entry) => entry.name)) {
        const keys = [...showcaseScope('streamwave', 'streaming-platform', view)!.documents.keys()]
          .filter((id) => id.startsWith('configuration:'))
          .map((id) => id.slice('configuration:'.length))
          .filter((key) => showcase.keys(view).has(key))
        for (const key of keys) {
          await expect(resolved(view, key), `${view} ${key}`).resolves.toBeDefined()
        }
      }
    })
  })
})
