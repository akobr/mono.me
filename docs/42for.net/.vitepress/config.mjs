import { defineConfig } from 'vitepress'
import { tabsMarkdownPlugin } from 'vitepress-plugin-tabs'

// https://vitepress.dev/reference/site-config
// A ```mermaid fence becomes the theme's Mermaid component (.vitepress/theme/Mermaid.vue), drawn in the browser.
function mermaidFences(md) {
  const fence = md.renderer.rules.fence
  let count = 0
  md.renderer.rules.fence = (tokens, index, options, env, self) => {
    const token = tokens[index]
    if (token.info.trim() === 'mermaid') {
      return `<Mermaid id="mermaid-${count++}" graph="${encodeURIComponent(token.content)}" />`
    }
    return fence(tokens, index, options, env, self)
  }
}

export default defineConfig({
  title: '42for.net',
  description: 'simple and clean .net',
  head: [
    ['link', { rel: 'icon', type: 'image/png', sizes: '32x32', href: '/favicon-32x32.png'}],
    ['link', { rel: 'icon', type: 'image/png', sizes: '16x16', href: '/favicon-16x16.png'}],
    // Analytics: PostHog EU without cookies, started by the theme (.vitepress/theme/analytics.js).
    [
      'script',
      {
        async: true,
        src: '/tooltips.js',
      }
    ]
  ],
  markdown: {
    config(md) {
      md.use(tabsMarkdownPlugin)
      md.use(mermaidFences)
    }
  },
  themeConfig: {
    // https://vitepress.dev/reference/default-theme-config
    logo: '/42-logo.png',
    lastUpdated: true,
    search: {
      provider: 'local'
    },
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Documentation', link: '/introduction' },
      { text: 'Monorepo', link: '/monorepo/introduction' },
      { text: '2S Platform', link: '/platform/introduction'},
      { text: 'Crumble', link: '/crumble/introduction'},
      { text: 'Codedoc', link: '/codedoc/introduction'},
    ],
    sidebar: [
      {
        text: 'Getting started',
        items: [
          { text: 'Introduction', link: '/introduction' },
          { text: 'Motivation', link: '/motivation' },
          { text: 'Install', link: '/install' },
        ]
      },
      {
        text: 'Architecture',
        items: [
          { text: 'Modulith', link: '/architecture/modulith' },
          { text: 'Pure distributed system', link: 'architecture/pure-distributed-system' },
          { text: 'No microservices', link: '/architecture/no-microservices' },
          { text: 'Which one to pick?', link: '/architecture/which-one-to-pick' },
        ]
      },
      {
        text: 'Monorepo',
        items: [
          { text: 'Introduction', link: '/monorepo/introduction' },
          { text: 'Why monorepo?', link: '/monorepo/why-monorepo' },
          { text: 'Road map', link: '/monorepo/road-map' },
        ]
      },
      {
        text: '2S Platform',
        items: [
          { text: 'Introduction', link: '/platform/introduction' },
          { text: 'Overview', link: '/platform/overview' },
          { text: 'Annotations', link: '/platform/annotations' },
          { text: 'Configuration', link: '/platform/configuration' },
          { text: 'How to model', link: '/platform/modeling' },
          { text: 'Using the platform', link: '/platform/using-the-platform' },
          { text: 'Administration UI', link: '/platform/admin-ui' },
          {
            text: 'Examples',
            collapsed: false,
            items: [
              { text: 'How the examples are built', link: '/platform/examples/' },
              { text: 'B2B SaaS', link: '/platform/examples/b2b-saas' },
              { text: 'B2C finance suite', link: '/platform/examples/b2c-finance' },
              { text: 'Distributed platform', link: '/platform/examples/distributed-platform' },
              { text: 'Versioned monolith', link: '/platform/examples/versioned-monolith' },
              { text: 'Physical product', link: '/platform/examples/physical-product' },
              { text: 'Device fleet', link: '/platform/examples/device-fleet' },
            ]
          },
          { text: 'Live demo', link: '/platform/live-demo' },
          { text: 'Road map', link: '/platform/road-map' },
        ]
      },
      {
        text: 'Crumble',
        items: [
          { text: 'Introduction', link: '/crumble/introduction' },
          { text: 'Overview', link: '/crumble/overview' },
          { text: 'Deployment', link: '/crumble/deployment' },
          { text: 'Crumbs', link: '/crumble/crumbs' },
          { text: 'Events', link: '/crumble/events' },
          { text: 'Road map', link: '/crumble/road-map' },
        ]
      },
      {
        text: 'Codedoc',
        items: [
          { text: 'Introduction', link: '/codedoc/introduction' },
          { text: 'Road map', link: '/codedoc/road-map' },
        ]
      },
      {
        text: 'CLI',
        items: [
          { text: 'mrepo', link: '/cli/mrepo' },
          { text: 'sform', link: '/cli/sform' },
          { text: 'GitHub Action', link: '/cli/github-action' },
        ]
      }/*,
      {
        text: 'Other talks',
        items: [
          { text: 'Automation testing', link: '/articles/automation-testing' },
          { text: 'Hell of dependencies', link: '/articles/dependency-hell' },
          { text: 'Infrastructure', link: '/articles/infrastructure' },
          { text: 'Technical decisions', link: '/articles/technical-decisions' },
          { text: 'Scarecrow of contracting', link: '/articles/contractors' },
        ]
      }*/
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/akobr/mono.me' },
      { icon: 'linkedin', link: 'https://www.linkedin.com/in/kobrales' },
    ],
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2024 Ales Kobr'
    }
  }
})
