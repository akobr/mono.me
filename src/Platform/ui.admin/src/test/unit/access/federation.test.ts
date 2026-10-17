import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { DOMWrapper } from '@vue/test-utils'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  createMachineAccess,
  deleteFederatedIssuer,
  getFederatedIssuer,
  getFederatedIssuers,
  getMachineAccess,
  getMachineAccesses,
  resetMachineAccess,
  setFederatedIssuer,
  setMachineAuthentication,
  setMachineFederationRules,
  type FederatedIssuer,
  type FederationRule,
  type MachineAccess,
  type MachineAccessCreate,
  type MachineAuthenticationPolicy,
} from '@42for.net/2splatform.sdk'
import {
  audienceDiffers,
  claimMap,
  claimRowError,
  claimRows,
  formatLifetime,
  githubAudience,
  isRuleComplete,
  issuerPlatform,
  newRuleAudience,
  placeholderIn,
  projectTokenEndpoint,
  ruleDraft,
  rulePlatform,
  toFederationRule,
} from '@/utils/federation'
import { credentialKindLabel, hasSecret, isFederated } from '@/utils/machines'
import FederatedSetupDialog from '@/components/access/FederatedSetupDialog.vue'
import FederationRulesDialog from '@/components/access/FederationRulesDialog.vue'
import MachineCreateDialog from '@/components/access/MachineCreateDialog.vue'
import TrustedIssuerDialog from '@/components/access/TrustedIssuerDialog.vue'
import TrustedIssuersTable from '@/components/access/TrustedIssuersTable.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb } from '@/mock/db'
import { mountScreen, settle, type AppMountOptions } from '../../helpers'

// Workload identity federation in the admin UI (424-46): the helpers, the mock API and the dialogs.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const tokenEndpoint = 'http://api.test/api/v1/acme/billing/access/token'
const githubRule = (subject = 'repo:acme/billing:ref:refs/heads/main'): FederationRule => ({
  Issuer: 'github',
  Audience: tokenEndpoint,
  Subject: subject,
})
/** The token endpoint at another address, as New machine presets it when the UI reaches the API through a proxy. */
const proxiedEndpoint = 'https://admin.contoso.com/api/v1/acme/billing/access/token'
/** An Azure managed identity rule: its audience is the app registration, not the token endpoint. */
const azureRule: FederationRule = { Issuer: 'azure', Audience: 'api://1111-2222', Subject: '3333-4444' }
const kubernetesRule: FederationRule = {
  Issuer: 'aks',
  Audience: proxiedEndpoint,
  Subject: 'system:serviceaccount:billing:billing-sync',
}
const platformIssuers: FederatedIssuer[] = [
  { Name: 'aks', Issuer: 'https://westeurope.oic.prod-aks.azure.com/tenant/cluster/', KeySource: 'Discovery' },
  { Name: 'azure', Issuer: 'https://login.microsoftonline.com/tenant/v2.0', KeySource: 'Discovery' },
  { Name: 'github', Issuer: 'https://token.actions.githubusercontent.com', KeySource: 'Discovery' },
]
const serveIssuers = (issuers: FederatedIssuer[]) =>
  server.use(http.get('*/api/v1/:org/:project/access/issuers', () => HttpResponse.json(issuers)))

describe('federation helpers', () => {
  it('tells Federated machines apart: no secret, no reset', () => {
    expect(isFederated('Federated')).toBe(true)
    expect(isFederated('ApiKey')).toBe(false)
    expect(hasSecret('Federated')).toBe(false)
    for (const kind of ['ApiKey', 'Certificate', 'CertificateAndApiKey', 'ClientCredentials'] as const) {
      expect(hasSecret(kind)).toBe(true)
    }
    // A machine stored without a kind is an API key machine.
    expect(hasSecret(undefined)).toBe(true)
    expect(credentialKindLabel(undefined)).toBe('API key')
    expect(credentialKindLabel('CertificateAndApiKey')).toBe('Certificate and API key')
    expect(credentialKindLabel('ClientCredentials')).toBe('Client credentials')
    expect(credentialKindLabel('Federated')).toBe('Federated')
  })

  it('edits claim maps as rows of one value each', () => {
    const rows = claimRows({ repository_id: ['74', '75'], '/kubernetes.io/namespace': ['billing'] })
    expect(rows).toEqual([
      { claim: 'repository_id', value: '74' },
      { claim: 'repository_id', value: '75' },
      { claim: '/kubernetes.io/namespace', value: 'billing' },
    ])
    expect(claimMap([...rows, { claim: ' repository_id ', value: '74' }, { claim: '', value: '' }])).toEqual({
      repository_id: ['74', '75'],
      '/kubernetes.io/namespace': ['billing'],
    })
    expect(claimMap([{ claim: '', value: ' ' }])).toBeUndefined()
    expect(claimRowError({ claim: 'owner_id', value: '' })).toBe('Enter the accepted value of owner_id.')
    expect(claimRowError({ claim: '', value: 'team_1' })).toBe('Enter the claim name or a JSON pointer.')
    expect(claimRowError({ claim: '', value: '' })).toBeUndefined()
  })

  it('needs an issuer, an audience, a subject and complete claim rows for a rule', () => {
    const rule = ruleDraft(undefined, tokenEndpoint)
    expect(rule).toEqual({ issuer: '', audience: tokenEndpoint, subject: '', claims: [] })
    expect(isRuleComplete(rule)).toBe(false)
    expect(isRuleComplete({ ...rule, issuer: 'github', subject: 'repo:acme/billing:*' })).toBe(true)
    expect(isRuleComplete({ ...rule, issuer: 'github', subject: 'repo:<owner>/<repo>:*' })).toBe(false)
    expect(
      isRuleComplete({ ...rule, issuer: 'github', subject: 'x', claims: [{ claim: 'repository_id', value: '' }] }),
    ).toBe(false)
    expect(
      toFederationRule({
        issuer: 'github',
        audience: ` ${tokenEndpoint} `,
        subject: ' repo:acme/billing:* ',
        claims: [{ claim: 'repository_id', value: '74' }],
      }),
    ).toEqual({ Issuer: 'github', Audience: tokenEndpoint, Subject: 'repo:acme/billing:*', Claims: { repository_id: ['74'] } })
    expect(ruleDraft(githubRule())).toMatchObject({ issuer: 'github', subject: 'repo:acme/billing:ref:refs/heads/main' })
  })

  it('classifies issuers by host like the API, and finds template placeholders', () => {
    expect(issuerPlatform({ Issuer: 'https://token.actions.githubusercontent.com/acme-enterprise' })).toBe('github')
    expect(issuerPlatform({ Issuer: 'https://token.actions.octo.ghe.com' })).toBe('github')
    expect(issuerPlatform({ Issuer: 'https://oidc.vercel.com/acme' })).toBe('vercel')
    expect(issuerPlatform({ Issuer: 'accounts.google.com' })).toBe('google')
    expect(issuerPlatform({ Issuer: 'https://sts.windows.net/tenant/' })).toBe('azure')
    expect(issuerPlatform({ Issuer: 'https://abc.tokens.sts.global.api.aws' })).toBe('aws')
    expect(issuerPlatform({ Issuer: 'https://westeurope.oic.prod-aks.azure.com/t/c/' })).toBe('kubernetes')
    expect(issuerPlatform({ Issuer: 'https://kubernetes.default.svc', KeySource: 'Jwks' })).toBe('kubernetes')
    expect(issuerPlatform({ Issuer: 'https://idp.example.com' })).toBe('custom')
    expect(issuerPlatform(undefined)).toBe('custom')
    expect(placeholderIn('https://oidc.vercel.com/<team-slug>')).toBe('<team-slug>')
    expect(placeholderIn('https://oidc.vercel.com/acme')).toBeUndefined()
    expect(projectTokenEndpoint('https://api.example.com/api/', 'acme', 'billing')).toBe(
      'https://api.example.com/api/v1/acme/billing/access/token',
    )
    expect(formatLifetime(undefined)).toBe('1 h (default)')
    expect(formatLifetime(86_400)).toBe('24 h')
    expect(formatLifetime(90)).toBe('90 s')
    expect(formatLifetime(600)).toBe('10 min')
  })

  it('finds the rules of each platform, flags another audience and keeps one audience per machine', () => {
    expect(rulePlatform(azureRule, platformIssuers)).toBe('azure')
    expect(rulePlatform(kubernetesRule, platformIssuers)).toBe('kubernetes')
    expect(rulePlatform({ Issuer: 'deleted' }, platformIssuers)).toBeUndefined()
    expect(rulePlatform(azureRule, undefined)).toBeUndefined()

    // The GitHub step requests the audience of the GitHub rule, whichever rule comes first.
    const mixed = { TokenEndpoint: tokenEndpoint, FederationRules: [azureRule, githubRule()] }
    expect(githubAudience(mixed, platformIssuers)).toBe(tokenEndpoint)
    expect(githubAudience({ ...mixed, FederationRules: [azureRule, kubernetesRule] }, platformIssuers)).toBeUndefined()
    // Without the issuers: the rule whose audience is the token endpoint, else rule 1.
    expect(githubAudience(mixed, undefined)).toBe(tokenEndpoint)
    expect(githubAudience({ ...mixed, FederationRules: [azureRule, kubernetesRule] }, undefined)).toBe(
      'api://1111-2222',
    )

    // A rule of a platform that requests the token endpoint is flagged when its audience is another; Azure rules,
    // rules of a deleted issuer and machines without a known token endpoint are not.
    expect(audienceDiffers(githubRule(), platformIssuers, tokenEndpoint)).toBe(false)
    expect(audienceDiffers(kubernetesRule, platformIssuers, tokenEndpoint)).toBe(true)
    expect(audienceDiffers(azureRule, platformIssuers, tokenEndpoint)).toBe(false)
    expect(audienceDiffers({ ...kubernetesRule, Issuer: 'deleted' }, platformIssuers, tokenEndpoint)).toBe(false)
    expect(audienceDiffers(kubernetesRule, undefined, tokenEndpoint)).toBe(false)
    expect(audienceDiffers(kubernetesRule, platformIssuers, undefined)).toBe(false)

    // New rules take the audience of the first rule that is not Azure's, else the token endpoint.
    expect(newRuleAudience([azureRule, kubernetesRule], platformIssuers, tokenEndpoint)).toBe(proxiedEndpoint)
    expect(newRuleAudience([azureRule], platformIssuers, tokenEndpoint)).toBe(tokenEndpoint)
    expect(newRuleAudience([kubernetesRule], [], tokenEndpoint)).toBe(tokenEndpoint)
    expect(newRuleAudience(undefined, platformIssuers, tokenEndpoint)).toBe(tokenEndpoint)
  })
})

describe('trusted issuers against the mock API', () => {
  it('lists the seeded issuer, creates, edits and deletes one', async () => {
    expect((await getFederatedIssuers('acme', 'billing')).map((issuer) => issuer.Name)).toEqual(['github'])

    const kubernetes: FederatedIssuer = {
      Issuer: 'https://westeurope.oic.prod-aks.azure.com/tenant/cluster/',
      MaxTokenLifetimeSeconds: 7200,
      RequiredClaims: { '/kubernetes.io/namespace': ['billing'] },
    }
    // The route name wins over the body.
    expect(await setFederatedIssuer('acme', 'billing', 'aks-prod', { ...kubernetes, Name: 'ignored' })).toMatchObject({
      Name: 'aks-prod',
      KeySource: 'Discovery',
      MaxTokenLifetimeSeconds: 7200,
    })
    expect((await getFederatedIssuers('acme', 'billing')).map((issuer) => issuer.Name)).toEqual(['aks-prod', 'github'])

    expect(
      await setFederatedIssuer('acme', 'billing', 'aks-prod', { ...kubernetes, Description: 'Production cluster' }),
    ).toMatchObject({ Description: 'Production cluster' })
    await expect(
      setFederatedIssuer('acme', 'billing', 'aks-prod', { ...kubernetes, Issuer: 'https://other.example.com' }),
    ).rejects.toMatchObject({
      status: 400,
      errorCode: 'InvalidFederation',
      message: 'The issuer of a trusted issuer cannot change; create a new one.',
    })

    await deleteFederatedIssuer('acme', 'billing', 'aks-prod')
    await expect(getFederatedIssuer('acme', 'billing', 'aks-prod')).rejects.toMatchObject({ status: 404 })
    await expect(deleteFederatedIssuer('acme', 'billing', 'aks-prod')).rejects.toMatchObject({ status: 404 })
  })

  it('answers the main validation messages of the API', async () => {
    await expect(
      setFederatedIssuer('acme', 'billing', 'insecure', { Issuer: 'http://idp.example.com' }),
    ).rejects.toMatchObject({
      status: 400,
      message: 'The issuer http://idp.example.com cannot be discovered: it must use https.',
    })
    await expect(setFederatedIssuer('acme', 'billing', 'Bad_Name', { Issuer: 'https://idp.example.com' })).rejects.toMatchObject(
      { status: 400 },
    )
    await expect(
      setFederatedIssuer('acme', 'billing', 'cluster', { Issuer: 'https://kubernetes.default.svc', KeySource: 'Jwks' }),
    ).rejects.toMatchObject({ message: 'The JWKS is required when the key source is Jwks.' })
    await expect(
      setFederatedIssuer('acme', 'billing', 'cluster', {
        Issuer: 'https://kubernetes.default.svc',
        KeySource: 'Jwks',
        Jwks: JSON.stringify({ keys: [{ kty: 'RSA', kid: 'k1', n: 'x', e: 'AQAB', d: 'secret' }] }),
      }),
    ).rejects.toMatchObject({ message: 'The JWKS is not valid: key 1 has the private member d; paste public keys only.' })
    await expect(
      setFederatedIssuer('acme', 'billing', 'idp', { Issuer: 'https://idp.example.com', MaxTokenLifetimeSeconds: 10 }),
    ).rejects.toMatchObject({ message: 'The maximum token lifetime must be between 60 and 86400 seconds.' })
    await expect(
      setFederatedIssuer('acme', 'billing', 'idp', { Issuer: 'https://idp.example.com', RequiredClaims: { sub: ['x'] } }),
    ).rejects.toMatchObject({ message: 'Required claims: the claim sub cannot be pinned; use Subject and Audience.' })
  })

  it('keeps a project at 20 trusted issuers', async () => {
    for (let index = 1; index < 20; index++) {
      await setFederatedIssuer('acme', 'billing', `idp-${index}`, { Issuer: `https://idp${index}.example.com` })
    }
    await expect(
      setFederatedIssuer('acme', 'billing', 'idp-20', { Issuer: 'https://idp20.example.com' }),
    ).rejects.toMatchObject({ status: 400, message: 'A project has at most 20 trusted issuers.' })
  })

  it('refuses to re-create a deleted issuer that rules still name, and lets only Administrators write', async () => {
    // The kill switch works while the seeded federated machine uses the issuer...
    await deleteFederatedIssuer('acme', 'billing', 'github')
    // ...but its name stays taken until those rules change.
    await expect(
      setFederatedIssuer('acme', 'billing', 'github', { Issuer: 'https://token.actions.githubusercontent.com' }),
    ).rejects.toMatchObject({
      status: 409,
      errorCode: 'FederatedIssuerInUse',
      message:
        "Federation rules still reference the trusted issuer 'github' (machines mach-release-pipeline); change those rules or delete the machines before creating it again.",
    })

    // The user contributes to acme.shipping: reading is allowed, writing is not.
    expect(await getFederatedIssuers('acme', 'shipping')).toHaveLength(1)
    await expect(
      setFederatedIssuer('acme', 'shipping', 'idp', { Issuer: 'https://idp.example.com' }),
    ).rejects.toMatchObject({ status: 403 })
    await expect(deleteFederatedIssuer('acme', 'shipping', 'github')).rejects.toMatchObject({ status: 403 })
  })
})

describe('federated machines against the mock API', () => {
  it('creates a machine without a secret, with its rules and the token endpoint', async () => {
    const machine = await createMachineAccess('acme', 'billing', {
      Scope: 'DefaultRead',
      CredentialKind: 'Federated',
      FederationRules: [githubRule()],
    })

    expect(machine).toMatchObject({
      CredentialKind: 'Federated',
      AccessKey: '',
      TokenEndpoint: tokenEndpoint,
      FederationRules: [githubRule()],
    })
    expect(machine.ObjectId).toBe(machine.Id)
    expect(machine.Certificate).toBeUndefined()
    expect(machine.LastRenewalAt).toBeUndefined()
    // Get and list build the token endpoint too.
    expect((await getMachineAccess('acme', 'billing', machine.Id!)).TokenEndpoint).toBe(tokenEndpoint)
    const listed = await getMachineAccesses('acme', 'billing')
    expect(listed.find((entry) => entry.Id === machine.Id)?.TokenEndpoint).toBe(tokenEndpoint)
    expect(listed.find((entry) => entry.Id === 'mach-invoicing-worker')?.TokenEndpoint).toBeUndefined()
  })

  it('validates the rules of a Federated machine and refuses them on other kinds', async () => {
    const create = (body: MachineAccessCreate) => createMachineAccess('acme', 'billing', { Scope: 'DefaultRead', ...body })

    await expect(create({ CredentialKind: 'Federated' })).rejects.toMatchObject({
      status: 400,
      errorCode: 'InvalidFederation',
      message: 'A Federated machine needs at least one federation rule.',
    })
    await expect(
      create({ CredentialKind: 'Federated', FederationRules: [{ ...githubRule(), Audience: 'https://github.com/acme' }] }),
    ).rejects.toMatchObject({ message: 'Rule 1: the audience https://github.com/acme belongs to another service.' })
    await expect(
      create({ CredentialKind: 'Federated', FederationRules: [githubRule(), { ...githubRule(), Issuer: 'gitlab' }] }),
    ).rejects.toMatchObject({ message: "Rule 2: the trusted issuer 'gitlab' doesn't exist in the project." })
    await expect(
      create({ CredentialKind: 'Federated', FederationRules: [{ ...githubRule(), Subject: 'repo:acme/*/x' }] }),
    ).rejects.toMatchObject({
      message: 'Rule 1: the subject can contain * only once, as its last character, right after : or /.',
    })
    await expect(create({ CredentialKind: 'ApiKey', FederationRules: [githubRule()] })).rejects.toMatchObject({
      status: 400,
      errorCode: 'MachineNotFederated',
      message: 'Only Federated machines have federation rules.',
    })

    // A GitHub issuer without the organization pin needs one in the rule.
    await setFederatedIssuer('acme', 'billing', 'github-any', { Issuer: 'https://token.actions.githubusercontent.com' })
    await expect(
      create({ CredentialKind: 'Federated', FederationRules: [{ ...githubRule(), Issuer: 'github-any' }] }),
    ).rejects.toMatchObject({ message: expect.stringContaining('Rule 1: a rule of a GitHub Actions issuer must pin') })
    await expect(
      create({
        CredentialKind: 'Federated',
        FederationRules: [{ ...githubRule(), Issuer: 'github-any', Claims: { repository_id: ['74'] } }],
      }),
    ).resolves.toMatchObject({ CredentialKind: 'Federated' })
  })

  it('replaces the rules, and has no secret to reset', async () => {
    const rules = [githubRule('repo:acme/billing:environment:staging'), githubRule('repo:acme/billing:pull_request')]
    const machine = await setMachineFederationRules('acme', 'billing', 'mach-release-pipeline', rules)
    expect(machine).toMatchObject({ FederationRules: rules, TokenEndpoint: tokenEndpoint })
    expect(loadDb().machines?.['acme.billing']?.find((entry) => entry.Id === 'mach-release-pipeline')?.FederationRules).toEqual(
      rules,
    )

    await expect(setMachineFederationRules('acme', 'billing', 'mach-release-pipeline', [])).rejects.toMatchObject({
      status: 400,
      message: 'A Federated machine needs at least one federation rule.',
    })
    await expect(setMachineFederationRules('acme', 'billing', 'mach-invoicing-worker', rules)).rejects.toMatchObject({
      status: 400,
      errorCode: 'MachineNotFederated',
    })
    await expect(setMachineFederationRules('acme', 'billing', 'mach-missing', rules)).rejects.toMatchObject({
      status: 404,
    })
    await expect(resetMachineAccess('acme', 'billing', 'mach-release-pipeline')).rejects.toMatchObject({
      status: 400,
      message: 'A Federated machine has no secret to reset. Change its federation rules instead.',
    })
  })

  it('lets the policy enable Federated', async () => {
    expect(
      await setMachineAuthentication('acme.billing', { EnabledCredentialKinds: ['Federated', 'ApiKey'] }),
    ).toMatchObject({ EnabledCredentialKinds: ['ApiKey', 'Federated'], CredentialKind: 'Federated' })
  })
})

/** The form control a field label names (UFormField links them with for/id). */
function control<T extends HTMLElement = HTMLInputElement>(label: string): T {
  const element = [...document.body.querySelectorAll('label')].find((entry) => entry.textContent?.trim() === label)
  expect(element, `a field labelled ${label}`).toBeDefined()
  return document.getElementById(element!.htmlFor) as T
}

/** Every form control a field label names, in document order. */
function controls<T extends HTMLElement = HTMLInputElement>(label: string): T[] {
  return [...document.body.querySelectorAll('label')]
    .filter((entry) => entry.textContent?.trim() === label)
    .map((entry) => document.getElementById(entry.htmlFor) as T)
}

const button = (label: string) =>
  [...document.body.querySelectorAll('button')].find((entry) => entry.textContent?.trim() === label)!

const text = () => document.body.textContent ?? ''

async function type(element: HTMLElement, value: string) {
  await new DOMWrapper(element).setValue(value)
  await settle(2)
}

/** Mounts a dialog with v-model:open in a screen, and opens it. */
async function openDialog(component: unknown, props: Record<string, unknown>, options?: AppMountOptions) {
  const open = ref(false)
  const events: Record<string, unknown[]> = {}
  const Harness = defineComponent({
    setup: () => () =>
      h(component as never, {
        ...props,
        open: open.value,
        'onUpdate:open': (value: boolean) => (open.value = value),
        onCreated: (value: unknown) => (events.created ??= []).push(value),
        onSaved: (value: unknown) => (events.saved ??= []).push(value),
      }),
  })
  const screen = await mountScreen(Harness, {}, options)
  open.value = true
  await settle(10)
  return { ...screen, open, events }
}

describe('MachineCreateDialog for a Federated machine', () => {
  const policy: MachineAuthenticationPolicy = { EnabledCredentialKinds: ['ApiKey', 'Federated'], CredentialKind: 'Federated' }

  it('edits a rule instead of certificate fields and creates the machine once the rule is complete', async () => {
    const bodies: MachineAccessCreate[] = []
    server.use(
      http.post('*/api/v1/:org/:project/access/machines', async ({ request }) => {
        bodies.push((await request.json()) as MachineAccessCreate)
        return HttpResponse.json({ Id: 'm1', AccessKey: '', CredentialKind: 'Federated' }, { status: 201 })
      }),
    )
    const screen = await openDialog(MachineCreateDialog, { policy })

    expect(text()).toContain('Rule 1')
    expect(text()).not.toContain('Certificate lifetime (days)')
    // The project's token endpoint is the audience, and the only trusted issuer is picked.
    expect(control('Audience').value).toBe(tokenEndpoint)
    expect(text()).toContain('repository_owner_id = 65')
    expect(button('Create').disabled).toBe(true)

    await type(control('Subject'), 'repo:acme/billing:environment:production')
    expect(button('Create').disabled).toBe(false)
    // A half-filled claim row blocks Create again.
    button('repository_id').click()
    await settle(4)
    expect(text()).toContain('Enter the accepted value of repository_id.')
    expect(button('Create').disabled).toBe(true)
    await type(document.querySelector<HTMLInputElement>('input[aria-label="Value 1"]')!, '74')
    expect(button('Create').disabled).toBe(false)

    button('Create').click()
    await settle(6)
    expect(bodies[0]).toMatchObject({
      CredentialKind: 'Federated',
      CertificateLifetimeDays: null,
      FederationRules: [
        {
          Issuer: 'github',
          Audience: tokenEndpoint,
          Subject: 'repo:acme/billing:environment:production',
          Claims: { repository_id: ['74'] },
        },
      ],
    })
    expect(screen.events.created).toHaveLength(1)
    screen.host.unmount()
  })

  it('shows the message of the API in the dialog', async () => {
    const screen = await openDialog(MachineCreateDialog, { policy })
    await type(control('Audience'), 'sts.amazonaws.com')
    await type(control('Subject'), 'repo:acme/billing:*')
    button('Create').click()
    await settle(8)

    expect(text()).toContain('Rule 1: the audience sts.amazonaws.com belongs to another service.')
    expect(screen.events.created).toBeUndefined()
    screen.host.unmount()
  })

  it('sends Administrators to the trusted issuers and tells Contributors to ask one', async () => {
    server.use(http.get('*/api/v1/:org/:project/access/issuers', () => HttpResponse.json([])))

    const admin = await openDialog(MachineCreateDialog, { policy })
    expect(text()).toContain('No trusted issuers')
    const link = [...document.body.querySelectorAll('a')].find((entry) => entry.textContent?.includes('Add a trusted issuer'))
    expect(link?.getAttribute('href')).toBe('/acme/billing/machines#trusted-issuers')
    expect(button('Create').disabled).toBe(true)
    // Following it closes the dialog and goes to the section (test/e2e checks that the section keeps the focus).
    link!.click()
    await settle(6)
    expect(admin.open.value).toBe(false)
    expect(admin.router.currentRoute.value.fullPath).toBe('/acme/billing/machines#trusted-issuers')
    admin.host.unmount()

    const contributor = await openDialog(MachineCreateDialog, { policy }, { accessMap: { 'acme.billing': 'Contributor' } })
    expect(text()).toContain('Ask an Administrator of the project to add one.')
    expect(text()).not.toContain('Add a trusted issuer')
    contributor.host.unmount()
  })
})

describe('TrustedIssuerDialog', () => {
  it('starts from the GitHub Actions template and adds the issuer once the organization is pinned', async () => {
    const screen = await openDialog(TrustedIssuerDialog, { names: ['github'] })

    expect(control('Issuer').value).toBe('https://token.actions.githubusercontent.com')
    // The seeded project has a github issuer already, so the name gets a number.
    expect(control('Name').value).toBe('github-2')
    expect(text()).toContain('repository_owner_id is its numeric id')
    expect(text()).toContain('Enter the accepted value of repository_owner_id.')
    expect(button('Add issuer').disabled).toBe(true)

    await type(document.querySelector<HTMLInputElement>('input[aria-label="Value 1"]')!, '65')
    expect(button('Add issuer').disabled).toBe(false)
    button('Add issuer').click()
    await settle(8)

    expect(loadDb().issuers?.['acme.billing']?.find((issuer) => issuer.Name === 'github-2')).toMatchObject({
      Issuer: 'https://token.actions.githubusercontent.com',
      RequiredClaims: { repository_owner_id: ['65'] },
    })
    expect(screen.events.saved).toHaveLength(1)
    expect(screen.open.value).toBe(false)
    screen.host.unmount()
  })

  it('checks the name and template placeholders, and shows the 400 and 409 messages of the API', async () => {
    const screen = await openDialog(TrustedIssuerDialog, { names: ['github'] })

    await type(control('Name'), 'github')
    expect(text()).toContain('The trusted issuer github exists already; edit it in the list.')
    await type(control('Name'), 'Not valid')
    expect(text()).toContain('Use 1 to 63 lower-case letters')
    await type(control('Name'), 'custom')
    await type(control('Issuer'), 'https://oidc.vercel.com/<team-slug>')
    expect(text()).toContain('Replace <team-slug> with the value of your platform.')
    expect(button('Add issuer').disabled).toBe(true)

    await type(control('Issuer'), 'http://idp.example.com')
    ;(document.querySelector('button[aria-label="Remove claim 1"]') as HTMLButtonElement).click()
    await settle(4)
    button('Add issuer').click()
    await settle(8)
    expect(text()).toContain('The issuer http://idp.example.com cannot be discovered: it must use https.')
    expect(screen.open.value).toBe(true)

    server.use(
      http.put('*/api/v1/:org/:project/access/issuers/:name', () =>
        HttpResponse.json(
          { Message: "Federation rules still reference the trusted issuer 'custom'.", ErrorCode: 'FederatedIssuerInUse' },
          { status: 409 },
        ),
      ),
    )
    await type(control('Issuer'), 'https://idp.example.com')
    button('Add issuer').click()
    await settle(8)
    expect(text()).toContain("Federation rules still reference the trusted issuer 'custom'.")
    screen.host.unmount()
  })

  it('edits an issuer without changing its issuer', async () => {
    const issuer = loadDb().issuers!['acme.billing']![0]!
    const screen = await openDialog(TrustedIssuerDialog, { issuer, names: ['github'] })

    expect(control('Name').disabled).toBe(true)
    expect(control('Issuer').readOnly).toBe(true)
    expect(text()).toContain('The issuer cannot change')
    expect(document.querySelector<HTMLInputElement>('input[aria-label="Value 1"]')!.value).toBe('65')

    await type(control('Description'), 'GitHub Actions of acme and its forks')
    button('Save').click()
    await settle(8)
    expect(loadDb().issuers?.['acme.billing']?.[0]).toMatchObject({
      Name: 'github',
      Issuer: 'https://token.actions.githubusercontent.com',
      Description: 'GitHub Actions of acme and its forks',
    })
    expect(screen.open.value).toBe(false)
    screen.host.unmount()
  })
})

describe('FederationRulesDialog', () => {
  const machine = (): MachineAccess => ({
    ...loadDb().machines!['acme.billing']!.find((entry) => entry.Id === 'mach-release-pipeline')!,
    TokenEndpoint: tokenEndpoint,
  })

  it('replaces the rules of the machine', async () => {
    const screen = await openDialog(FederationRulesDialog, { machine: machine() })

    expect(control('Subject').value).toBe('repo:acme@65/billing@74:environment:production')
    await type(control('Subject'), 'repo:acme@65/billing@74:environment:staging')
    button('Add rule').click()
    await settle(6)
    expect(text()).toContain('Rule 2')
    expect(button('Save rules').disabled).toBe(true)
    ;(document.querySelector('button[aria-label="Remove rule 2"]') as HTMLButtonElement).click()
    await settle(4)

    button('Save rules').click()
    await settle(8)
    expect(machine().FederationRules).toEqual([
      {
        Issuer: 'github',
        Audience: machine().FederationRules![0]!.Audience,
        Subject: 'repo:acme@65/billing@74:environment:staging',
        Claims: { repository_id: ['74'] },
      },
    ])
    expect(screen.open.value).toBe(false)
    screen.host.unmount()
  })

  it('adds rules with the audience the machine uses already, so its rules never mix two', async () => {
    serveIssuers(platformIssuers)
    // Created where the UI reaches the API through a proxy: the rule's audience is not the API's token endpoint.
    const proxied = { ...machine(), FederationRules: [{ ...githubRule(), Audience: proxiedEndpoint }] }
    const screen = await openDialog(FederationRulesDialog, { machine: proxied })
    button('Add rule').click()
    await settle(6)
    expect(controls('Audience').map((input) => input.value)).toEqual([proxiedEndpoint, proxiedEndpoint])
    screen.host.unmount()

    // An Azure rule's audience is the app registration: a new rule takes the token endpoint.
    const azure = await openDialog(FederationRulesDialog, { machine: { ...machine(), FederationRules: [azureRule] } })
    button('Add rule').click()
    await settle(6)
    expect(controls('Audience').map((input) => input.value)).toEqual(['api://1111-2222', tokenEndpoint])
    azure.host.unmount()
  })

  it('shows the message of the API and keeps the dialog open', async () => {
    const screen = await openDialog(FederationRulesDialog, { machine: machine() })
    await type(control('Subject'), 'repo:acme*')
    button('Save rules').click()
    await settle(8)

    expect(text()).toContain('Rule 1: the subject can contain * only once')
    expect(screen.open.value).toBe(true)
    screen.host.unmount()
  })
})

describe('FederatedSetupDialog', () => {
  const snippet = (label: string) => document.querySelector(`pre[aria-label="${label}"]`)?.textContent ?? undefined

  it('shows the machine id, the token endpoint and the exchange instead of a secret', async () => {
    const closed = vi.fn()
    const view = await mountScreen(FederatedSetupDialog, {
      machine: { Id: 'mach-1', CredentialKind: 'Federated', TokenEndpoint: tokenEndpoint, FederationRules: [githubRule()] },
      onClose: closed,
    })
    await settle(6)

    expect(text()).toContain('Federated machine created')
    expect(text()).toContain('This machine has no secret')
    expect(text()).not.toContain('Shown only once')
    expect(text()).toContain(`curl -sf ${tokenEndpoint}`)
    expect(text()).toContain('-d client_id=mach-1')
    expect(text()).toContain('subject_token_type=urn:ietf:params:oauth:token-type:jwt')
    expect(text()).toContain('id-token: write')
    expect(text()).toContain(`AUDIENCE: ${tokenEndpoint}`)
    expect(text()).toContain('Rule 1:')
    expect(snippet('Token exchange (curl)')).toContain(
      `# PLATFORM_TOKEN: a token of the trusted issuer, requested with the audience ${tokenEndpoint}`,
    )
    expect(text()).not.toContain('not the token endpoint')

    button('Done').click()
    expect(closed).toHaveBeenCalledOnce()
    view.host.unmount()
  })

  it('requests the GitHub token with the audience of the GitHub rule, not of rule 1', async () => {
    serveIssuers(platformIssuers)
    const view = await mountScreen(FederatedSetupDialog, {
      machine: {
        Id: 'mach-1',
        CredentialKind: 'Federated',
        TokenEndpoint: tokenEndpoint,
        FederationRules: [azureRule, githubRule()],
      },
    })
    await settle(6)

    expect(snippet('GitHub Actions step')).toContain(`AUDIENCE: ${tokenEndpoint}`)
    expect(snippet('GitHub Actions step')).not.toContain('api://')
    // The exchange names the audience per trusted issuer.
    expect(snippet('Token exchange (curl)')).toContain(
      '# PLATFORM_TOKEN: a token of a trusted issuer, requested with the audience of its rule:\n' +
        '#   azure: api://1111-2222\n' +
        `#   github: ${tokenEndpoint}\n`,
    )
    // The Azure rule's audience is the app registration: it is not flagged.
    expect(text()).not.toContain('not the token endpoint')
    view.host.unmount()
  })

  it('has no GitHub step without a GitHub rule, and flags an audience other than the token endpoint', async () => {
    serveIssuers(platformIssuers)
    const view = await mountScreen(FederatedSetupDialog, {
      machine: {
        Id: 'mach-1',
        CredentialKind: 'Federated',
        TokenEndpoint: tokenEndpoint,
        FederationRules: [azureRule, kubernetesRule],
      },
    })
    await settle(6)

    expect(snippet('Token exchange (curl)')).toContain(`#   aks: ${proxiedEndpoint}`)
    expect(snippet('GitHub Actions step')).toBeUndefined()
    const flagged = [...document.body.querySelectorAll('li')].filter((item) =>
      item.textContent?.includes('not the token endpoint'),
    )
    expect(flagged.map((item) => item.textContent?.trim().slice(0, 7))).toEqual(['Rule 2:'])
    view.host.unmount()
  })

  it('falls back to the rule for the token endpoint while the issuers cannot be read', async () => {
    server.use(http.get('*/api/v1/:org/:project/access/issuers', () => new HttpResponse(null, { status: 403 })))
    const view = await mountScreen(FederatedSetupDialog, {
      machine: {
        Id: 'mach-1',
        CredentialKind: 'Federated',
        TokenEndpoint: tokenEndpoint,
        FederationRules: [azureRule, githubRule()],
      },
    })
    await settle(6)

    expect(snippet('GitHub Actions step')).toContain(`AUDIENCE: ${tokenEndpoint}`)
    expect(text()).not.toContain('not the token endpoint')
    view.host.unmount()
  })
})

describe('TrustedIssuersTable', () => {
  it('warns while the policy does not enable Federated machines', async () => {
    const enabled = await mountScreen(TrustedIssuersTable, {})
    await settle(6)
    expect(text()).toContain('https://token.actions.githubusercontent.com')
    expect(text()).not.toContain('Federated machines are disabled')
    enabled.host.unmount()

    server.use(
      http.get('*/api/v1/access/points/:key/machine-authentication', () =>
        HttpResponse.json({ EnabledCredentialKinds: ['ApiKey'], CredentialKind: 'ApiKey' }),
      ),
    )
    const disabled = await mountScreen(TrustedIssuersTable, {})
    await settle(6)
    expect(text()).toContain('Federated machines are disabled')
    // The issuers stay listed and editable.
    expect(text()).toContain('https://token.actions.githubusercontent.com')
    disabled.host.unmount()
  })

  it('deletes an issuer once its name is typed, and lists the rest', async () => {
    const view = await mountScreen(TrustedIssuersTable, {})
    await settle(6)

    ;(document.querySelector('button[aria-label="Delete github"]') as HTMLButtonElement).click()
    await settle(4)
    expect(text()).toContain('Rules that use it stop matching new exchanges within a minute')
    expect(button('Delete').disabled).toBe(true)
    await type(control('Type github to confirm'), 'github')
    button('Delete').click()
    await settle(10)

    expect(loadDb().issuers?.['acme.billing']).toEqual([])
    expect(text()).toContain('Trusted issuer github deleted')
    expect(text()).toContain('No trusted issuers')
    expect(text()).not.toContain('https://token.actions.githubusercontent.com')
    view.host.unmount()
  })
})
