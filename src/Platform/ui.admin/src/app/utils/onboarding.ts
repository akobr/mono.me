import type { Exhibition } from '@42for.net/2splatform.sdk'

/**
 * The texts, the sub-processors and every link of the onboarding wizard (424-267), so a change of the wording is this
 * one file. The hosting and sign-in wording repeats docs/Platform/Deployment/decisions.md: change them together.
 */

export type OnboardingStep = 'welcome' | 'security' | 'exhibition' | 'organization'

/** The steps in their order; a deployment without an exhibition has no `exhibition` step. */
export const onboardingSteps: readonly OnboardingStep[] = ['welcome', 'security', 'exhibition', 'organization']

export const stepNames: Record<OnboardingStep, string> = {
  welcome: 'Welcome',
  security: 'Security',
  exhibition: 'Exhibition',
  organization: 'Organization',
}

/** The accessible name of a step in the step list; `number` counts from 1. */
export const stepLabel = (number: number, step: OnboardingStep) => `Step ${number}: ${stepNames[step]}`

/** The heading of the page; the name is personal data (ph-no-capture). */
export const welcomeLine = (name?: string) => (name ? `Welcome, ${name}` : 'Welcome')

/**
 * The release switch of the three safeguards the hosting statement names: blocked impersonation (424-262), the audit
 * log of access changes (424-263) and the recent sign-in for secrets (424-264). It turns true only once all three run
 * in the production API and the WorkOS dashboard check of 424-262 is recorded; until then the wizard does not claim
 * them.
 */
export const safeguardsLive: boolean = false

/** What WorkOS does, the purpose of its entry in the sub-processors, with or without the three safeguards. */
export function hostingStatement(live: boolean = safeguardsLive): string[] {
  return [
    'Signs in the operators of the 2S Platform and sends their invitation emails.',
    'It processes only their name, email, IP address and session data, and the email address of a person they invite.',
    ...(live
      ? [
          'Every authorization decision is made and recorded by 2S in the EU.',
          'Impersonation is blocked, access to secrets requires a recent sign-in, and every access change is in an append-only audit log.',
        ]
      : ['Every authorization decision is made by 2S in the EU.']),
    'Customers that need an EU-only identity provider can use their own SSO provider or host the platform themselves.',
  ]
}

export type SubProcessorRegion = 'EU only' | 'USA'

export interface SubProcessor {
  name: string
  purpose: string
  region: SubProcessorRegion
}

export const subProcessors: readonly SubProcessor[] = [
  { name: 'Microsoft Azure', purpose: 'Hosting, database, storage, secrets', region: 'EU only' },
  { name: 'Sentry', purpose: 'Error reporting', region: 'EU only' },
  { name: 'PostHog', purpose: 'Product analytics', region: 'EU only' },
  { name: 'WorkOS', purpose: hostingStatement().join(' '), region: 'USA' },
]

export interface OnboardingLink {
  label: string
  to: string
}

/** The discussion that the issue names for the details of the hosting. */
export const hostingLink: OnboardingLink = {
  label: 'Details of the EU hosting',
  to: 'https://claude.ai/share/c9fbe93d-61c4-466c-8490-2ba028fb1999',
}

export const securityLinks: readonly OnboardingLink[] = [
  {
    label: 'Authentication: Entra ID and Keycloak',
    to: 'https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#selecting-the-provider',
  },
  {
    label: 'Federated auth of machines',
    to: 'https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#machine-access-with-workload-identity-federation',
  },
  { label: 'sform', to: 'https://42for.net/cli/sform' },
  { label: 'GitHub Action', to: 'https://42for.net/cli/github-action' },
]

export const onboardingTexts = {
  stepList: 'Onboarding steps',
  back: 'Back',
  next: 'Next',
  signOut: 'Sign out',
  welcome: {
    heading: 'Customer data stays in the EU',
    lead: 'All customer data, configuration and secrets are stored and processed only in the EU (Sweden).',
    ai: 'The same applies to all AI data processing within the platform.',
    subProcessors: 'Sub-processors',
  },
  security: {
    heading: 'Security first',
    lead: 'Security is the main focus of the 2S Platform.',
    machines: {
      title: 'Machines and pipelines',
      text: 'They need no stored secret. A machine exchanges its own platform token, for example a GitHub Actions OIDC token or a Kubernetes service account token, for a short-lived 2S token.',
    },
    people: {
      title: 'People',
      text: 'This platform signs you in with WorkOS AuthKit. A dedicated deployment can sign people in with an identity provider of your own instead:',
      // The name is bold; the rest follows it as written, with its own space or comma.
      providers: [
        { name: 'Entra ID', rest: ' in your own tenant' },
        { name: 'Keycloak', rest: ', self-hosted, for example in the EU' },
      ],
      limit:
        'Sign-in is then fully under your control, and WorkOS is not involved. Such a deployment is used through sform and the API; the Admin UI is not available in it today.',
    },
  },
  exhibition: {
    heading: 'Join the exhibition',
    text: 'The exhibition is a demo project with the data of a fictional company. It shows the core concepts of the platform at work. You join as a Reader: you can browse its annotations, their configurations and the graph, and you cannot change anything.',
    choice: 'Join the exhibition',
    // The note names the button of the last step between its two parts.
    noteStart:
      'This step is optional. To join only the exhibition and create your own organization later, check the box and choose',
    noteEnd: 'in the last step.',
  },
  organization: {
    heading: 'Create your organization',
    text: 'Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.',
    submit: 'Create and continue',
    joining: 'You are joining the exhibition.',
    exhibitionOnly: 'Continue with the exhibition only',
  },
  end: {
    joined: 'You joined the exhibition',
    open: 'Open the exhibition',
    joinFailed: 'Joining the exhibition failed',
    accountReady: 'Your account is ready.',
    tryAgain: 'Try again',
    withoutExhibition: 'Continue without the exhibition',
  },
} as const

/** What checking the box does, under its label. */
export const exhibitionChoiceDescription = (exhibition: Pick<Exhibition, 'Organization' | 'Project'>) =>
  `Adds you to ${exhibition.Organization}/${exhibition.Project} as a Reader. Its administrators see your name and email in the list of members; other visitors do not.`
