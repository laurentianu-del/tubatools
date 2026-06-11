import DefaultTheme from 'vitepress/theme'
import '../styles/index.css'
import ToolShowcase from './ToolShowcase.vue'
import DiskScan from './DiskScan.vue'
import CertBlock from './CertBlock.vue'
import HardwareInfo from './HardwareInfo.vue'

export default {
  ...DefaultTheme,
  enhanceApp({ app }) {
    app.component('ToolShowcase', ToolShowcase)
    app.component('DiskScan', DiskScan)
    app.component('CertBlock', CertBlock)
    app.component('HardwareInfo', HardwareInfo)
  },
}
