import { createApp } from 'vue'
import App from './App.vue'
import 'vuetify/styles'
import '@mdi/font/css/materialdesignicons.css'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'

const vuetify = createVuetify({
  components,
  directives,
  theme: {
    defaultTheme: 'dark',
    themes: {
      dark: {
        colors: {
          primary: '#60A5FA',
          secondary: '#4ADE80',
          background: '#1E1E2E',
          surface: '#2A2A3C',
        },
      },
      light: {
        colors: {
          primary: '#3B82F6',
          secondary: '#22C55E',
          background: '#F8F9FA',
          surface: '#FFFFFF',
        },
      },
    },
  },
})

createApp(App).use(vuetify).mount('#app')
