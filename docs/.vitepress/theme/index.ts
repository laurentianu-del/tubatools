import DefaultTheme from 'vitepress/theme'
import { gsap } from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
import '../styles/index.css'
import ToolShowcase from './ToolShowcase.vue'
import DiskScan from './DiskScan.vue'
import CertBlock from './CertBlock.vue'
import HardwareInfo from './HardwareInfo.vue'
import PageAnimator from './PageAnimator.vue'

gsap.registerPlugin(ScrollTrigger)

export default {
  ...DefaultTheme,
  enhanceApp({ app }) {
    app.component('ToolShowcase', ToolShowcase)
    app.component('DiskScan', DiskScan)
    app.component('CertBlock', CertBlock)
    app.component('HardwareInfo', HardwareInfo)
    app.component('PageAnimator', PageAnimator)
  },
}
