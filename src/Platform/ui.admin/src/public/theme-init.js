// Applies the stored color mode before the first paint (external file, so the CSP needs no 'unsafe-inline').
// The key and values are the ones of VueUse useDark/useColorMode, which Nuxt UI uses.
;(function () {
  try {
    var mode = localStorage.getItem('vueuse-color-scheme') || 'auto'
    var dark = mode === 'dark' || (mode === 'auto' && window.matchMedia('(prefers-color-scheme: dark)').matches)
    document.documentElement.classList.toggle('dark', dark)
  } catch {
    // storage blocked: keep the light default
  }
})()
