import DefaultTheme from 'vitepress/theme'
import _42Layout from './_42Layout.vue'
import { enhanceAppWithTabs } from 'vitepress-plugin-tabs/client'
import { startAnalytics } from './analytics'
import Mermaid from './Mermaid.vue'
import YouTubeVideo from './YouTubeVideo.vue'
import './_42styles.css'

export default {
    extends: DefaultTheme,
    Layout: _42Layout,
    enhanceApp({ app, router }) {
      enhanceAppWithTabs(app)
      app.component('Mermaid', Mermaid)
      app.component('YouTubeVideo', YouTubeVideo)
      startAnalytics(router)
    }
  }