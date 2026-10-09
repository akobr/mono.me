import { globalIgnores } from 'eslint/config'
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
import pluginVue from 'eslint-plugin-vue'
import pluginOxlint from 'eslint-plugin-oxlint'

// oxlint runs first (npm run lint); ESLint adds the Vue template rules oxlint cannot check yet,
// and eslint-plugin-oxlint turns off every rule oxlint already covers.
export default defineConfigWithVueTs(
  { name: 'app/files-to-lint', files: ['**/*.{ts,mts,vue}'] },
  globalIgnores([
    '**/dist/**',
    '**/coverage/**',
    'test-results/**',
    'playwright-report/**',
    'app/typed-router.d.ts',
    'app/auto-imports.d.ts',
    'app/components.d.ts',
  ]),
  pluginVue.configs['flat/recommended'],
  vueTsConfigs.recommended,
  {
    rules: {
      'vue/multi-word-component-names': 'off',
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/html-self-closing': 'off',
      'vue/html-indent': 'off',
      'vue/html-closing-bracket-newline': 'off',
      'vue/multiline-html-element-content-newline': 'off',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
    },
  },
  ...pluginOxlint.buildFromOxlintConfigFile('.oxlintrc.json'),
)
