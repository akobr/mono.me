import { describe, expect, it } from 'vitest'
import {
  exhibitionChoiceDescription,
  hostingLink,
  hostingStatement,
  onboardingSteps,
  onboardingTexts,
  safeguardsLive,
  securityLinks,
  stepLabel,
  stepNames,
  subProcessors,
  welcomeLine,
} from '@/utils/onboarding'

// The texts of the onboarding wizard (424-267, spec 3.3 to 3.6 and 8). The hosting statement has two forms: the
// wizard claims the three safeguards only once they run in production.

describe('the hosting statement', () => {
  const workos = [
    'Signs in the operators of the 2S Platform and sends their invitation emails.',
    'It processes only their name, email, IP address and session data, and the email address of a person they invite.',
  ]
  const identityProvider =
    'Customers that need an EU-only identity provider can use their own SSO provider or host the platform themselves.'

  it('does not claim the safeguards before they are live', () => {
    expect(hostingStatement(false)).toEqual([
      ...workos,
      'Every authorization decision is made by 2S in the EU.',
      identityProvider,
    ])
    expect(hostingStatement(false).join(' ')).not.toMatch(/recorded|[Ii]mpersonation|recent sign-in|audit log/)
  })

  it('names the three safeguards once they are live', () => {
    expect(hostingStatement(true)).toEqual([
      ...workos,
      'Every authorization decision is made and recorded by 2S in the EU.',
      'Impersonation is blocked, access to secrets requires a recent sign-in, and every access change is in an append-only audit log.',
      identityProvider,
    ])
  })

  it('is released without the safeguards', () => {
    expect(safeguardsLive).toBe(false)
    expect(hostingStatement()).toEqual(hostingStatement(false))
  })

  it('states where the data is in its heading and its first sentence', () => {
    expect(onboardingTexts.welcome.heading).toBe('Customer data stays in the EU')
    expect(onboardingTexts.welcome.lead).toBe(
      'All customer data, configuration and secrets are stored and processed only in the EU (Sweden).',
    )
    expect(onboardingTexts.welcome.ai).toBe('The same applies to all AI data processing within the platform.')
  })
})

describe('the sub-processors', () => {
  it('are the four of the deployment, with WorkOS as the only one outside the EU', () => {
    expect(subProcessors).toEqual([
      { name: 'Microsoft Azure', purpose: 'Hosting, database, storage, secrets', region: 'EU only' },
      { name: 'Sentry', purpose: 'Error reporting', region: 'EU only' },
      { name: 'PostHog', purpose: 'Product analytics', region: 'EU only' },
      {
        name: 'WorkOS',
        purpose: hostingStatement(false).join(' '),
        region: 'USA',
      },
    ])
  })
})

describe('the links of the wizard', () => {
  it('lead to the discussion of the hosting, the documentation of authentication and the tools', () => {
    expect(hostingLink).toEqual({
      label: 'Details of the EU hosting',
      to: 'https://claude.ai/share/c9fbe93d-61c4-466c-8490-2ba028fb1999',
    })
    expect(securityLinks).toEqual([
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
    ])
  })

  it('are https addresses, each with its own label', () => {
    const links = [hostingLink, ...securityLinks]

    for (const link of links) {
      expect(new URL(link.to).protocol, link.label).toBe('https:')
    }
    expect(new Set(links.map((link) => link.label)).size).toBe(links.length)
    expect(new Set(links.map((link) => link.to)).size).toBe(links.length)
  })
})

describe('the steps and their names', () => {
  it('are welcome, security, exhibition and organization', () => {
    expect(onboardingSteps).toEqual(['welcome', 'security', 'exhibition', 'organization'])
    expect(onboardingSteps.map((step) => stepNames[step])).toEqual([
      'Welcome',
      'Security',
      'Exhibition',
      'Organization',
    ])
  })

  it('are named by their number and name in the step list', () => {
    expect(stepLabel(1, 'welcome')).toBe('Step 1: Welcome')
    expect(stepLabel(3, 'organization')).toBe('Step 3: Organization')
  })
})

describe('the other texts', () => {
  it('greet the user by name, or nobody', () => {
    expect(welcomeLine('Ada Lovelace')).toBe('Welcome, Ada Lovelace')
    expect(welcomeLine()).toBe('Welcome')
    expect(welcomeLine('')).toBe('Welcome')
  })

  it('say what joining the exhibition does, with the project of the deployment', () => {
    expect(exhibitionChoiceDescription({ Organization: 'fourth-wall', Project: 'toons-and-tyrants' })).toBe(
      'Adds you to fourth-wall/toons-and-tyrants as a Reader. Its administrators see your name and email in the list of members; other visitors do not.',
    )
  })

  it('name the button of the last step in the note of the exhibition', () => {
    const { noteStart, noteEnd } = onboardingTexts.exhibition

    expect(`${noteStart} ${onboardingTexts.organization.exhibitionOnly} ${noteEnd}`).toBe(
      'This step is optional. To join only the exhibition and create your own organization later, check the box and choose Continue with the exhibition only in the last step.',
    )
  })

  it('name the own identity providers as the list shows them', () => {
    expect(onboardingTexts.security.people.providers.map((provider) => `${provider.name}${provider.rest}`)).toEqual([
      'Entra ID in your own tenant',
      'Keycloak, self-hosted, for example in the EU',
    ])
  })
})
