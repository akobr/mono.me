import type { Exhibition } from '@42for.net/2splatform.sdk'

/**
 * The texts, the sub-processors and every link of the onboarding wizard (424-267), so a change of the wording is this
 * one file. The hosting and sign-in wording repeats docs/Platform/Deployment/decisions.md: change them together.
 */

export type OnboardingStep = 'welcome' | 'residency' | 'security' | 'exhibition' | 'organization'

/** The steps in their order; a deployment without an exhibition has no `exhibition` step. */
export const onboardingSteps: readonly OnboardingStep[] = [
  'welcome',
  'residency',
  'security',
  'exhibition',
  'organization',
]

export const stepNames: Record<OnboardingStep, string> = {
  welcome: 'Welcome',
  residency: 'Residency',
  security: 'Security',
  exhibition: 'Exhibition',
  organization: 'Organization',
}

/** The accessible name of a step in the step list; `number` counts from 1. */
export const stepLabel = (number: number, step: OnboardingStep) => `Step ${number}: ${stepNames[step]}`

/** The heading of the page; the name is personal data (ph-no-capture). */
export const welcomeLine = (name?: string) => (name ? `Welcome, ${name}` : 'Welcome')

/** What WorkOS does, the purpose of its entry in the sub-processors. */
export const workosPurpose = [
  'Signs in the operators of the 2S Platform and sends their invitation emails.',
  'It processes only their name, email, IP address and session data, and the email address of a person they invite.',
].join(' ')

export type SubProcessorRegion = 'EU only' | 'USA'

export interface SubProcessor {
  name: string
  purpose: string
  /** A second paragraph under the purpose. */
  note?: string
  region: SubProcessorRegion
}

export const subProcessors: readonly SubProcessor[] = [
  { name: 'Microsoft Azure', purpose: 'Hosting, storage, secrets, logs', region: 'EU only' },
  { name: 'Sentry', purpose: 'Error reporting', region: 'EU only' },
  { name: 'PostHog', purpose: 'Product analytics', region: 'EU only' },
  {
    name: 'WorkOS',
    purpose: workosPurpose,
    note: 'Every authorization decision is made and recorded by 2S in the EU.',
    region: 'USA',
  },
]

export interface OnboardingLink {
  label: string
  to: string
}

/** The agreements a new customer accepts with the account (the organization form, or the join of the exhibition). */
export const agreementLinks = {
  license: { label: 'Customer License Agreement', to: 'https://42for.software/legal/customer-license-agreement' },
  dataProcessing: { label: 'Data Processing Agreement', to: 'https://42for.software/legal/data-processing-agreement' },
} as const

/** The links of the residency step. */
export const residencyLinks: readonly OnboardingLink[] = [
  { label: 'Cloud hosting', to: 'https://42for.net/platform/cloud' },
  { label: 'Sub-processors list', to: 'https://42for.software/legal/sub-processors' },
]

export const securityLinks: readonly OnboardingLink[] = [
  { label: 'Security details', to: 'https://42for.net/platform/security' },
  { label: 'Authentication of machines', to: 'https://42for.net/platform/auth-machine' },
  { label: 'Self-hosting option', to: 'https://42for.net/platform/self-hosting' },
]

export const onboardingTexts = {
  stepList: 'Onboarding steps',
  back: 'Back',
  next: 'Next',
  signOut: 'Sign out',
  welcome: {
    heading: 'Meet the 2S Platform',
    pricing:
      'The platform is free to use while we shape its pricing. When pricing arrives, it will apply to every customer, existing ones included, and you will hear from us well in advance. It will be based on usage, not on seats, with a generous free tier.',
    documentation: { label: 'Documentation', to: 'https://42for.net/platform/introduction' },
    motto:
      'Built to tame the configuration mayhem of complex systems and fast-growing application catalogs, AI-made ones included.',
  },
  residency: {
    heading: "Your platform's data stays in the EU",
    lead: 'All customer data, configuration, secrets and machine access are stored and processed only in the EU (Sweden).',
    ai: 'The same applies to all AI data processing within the platform.',
    subProcessors: 'Sub-processors',
  },
  security: {
    heading: 'Security first',
    lead: "Security isn't a feature we added later. It is the ground the 2S Platform stands on.",
    secrets: {
      title: 'Secrets',
      text: 'Keep them your way: encrypted with keys in an EU vault, with a key you bring yourself, or encrypted for the one recipient that needs them, so that nobody else can read them, not even us.',
    },
    machines: {
      title: 'Machines and pipelines',
      text: 'No stored passwords, no long-lived keys waiting to leak. A machine proves who it is with its own mTLS certificate or a federated token from the system it runs on, and gets exactly the access its scopes allow.',
    },
    people: {
      title: 'People',
      // The three safeguards: blocked impersonation (424-262), the recent sign-in for secrets (424-264) and the audit log
      // of access changes (424-263). The platform opens to the public only once all three run in production.
      text: 'You sign in with WorkOS AuthKit, and 2S alone decides what you may do. Impersonation is blocked, secrets ask for a recent sign-in, and every change of access lands in an append-only audit log.',
      identityProvider:
        'Need an EU-only identity provider? Bring your own SSO provider, or host the platform yourself.',
    },
    testing: {
      title: 'Security testing',
      text: 'Security is built into how we develop the platform, and our testing programme grows with it. We are rolling out automated scanning in our build pipeline and run manual security testing across our REST API, admin interface, CLI and MCP server. Our approach follows recognised industry standards, and every finding is tracked and resolved within defined timelines.',
    },
  },
  exhibition: {
    heading: 'Join the exhibition',
    text: 'Step into Fourth Wall Software, a fictional software house whose customers include Acme Corp and Globex. See how the platform keeps their CRM, HR and CAD solutions configured.',
    promise: 'Explore 30 annotations, configurations that resolve live, and the graph that ties them together.',
    choice: 'Show me the exhibition',
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
export const exhibitionChoiceDescription = (_exhibition?: Pick<Exhibition, 'Organization' | 'Project'>) =>
  'Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only.'
