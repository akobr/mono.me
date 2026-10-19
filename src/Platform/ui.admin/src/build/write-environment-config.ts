import { readFileSync, writeFileSync } from 'node:fs'
import { applyReplacements, environmentConfig } from './environment-config.ts'

// The "Environment configuration" step of the deploy job (.github/workflows/ui-admin.yml) runs it in the package
// folder after the build: node build/write-environment-config.ts (Node 24 strips the types).
const { appConfig, replacements } = environmentConfig(process.env)

const staticWebAppConfig = 'dist/staticwebapp.config.json'
const configured = applyReplacements(readFileSync(staticWebAppConfig, 'utf8'), replacements)

writeFileSync('dist/app-config.json', JSON.stringify(appConfig, null, 2))
writeFileSync(staticWebAppConfig, configured)
