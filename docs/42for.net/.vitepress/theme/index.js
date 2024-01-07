import DefaultTheme from 'vitepress/theme'
import _42Layout from './_42Layout.vue'
import { enhanceAppWithTabs } from 'vitepress-plugin-tabs/client'
import './_42styles.css'

export default {
    extends: DefaultTheme,
    Layout: _42Layout,
    enhanceApp({ app }) {
      enhanceAppWithTabs(app)
    }
  }