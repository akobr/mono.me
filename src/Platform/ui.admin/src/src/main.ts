import './assets/main.css'
import "v-network-graph/lib/style.css"

import { createApp } from 'vue'
import VNetworkGraph from "v-network-graph"

import App from './App.vue'
const app = createApp(App)

app.use(VNetworkGraph)
app.mount('#app')
