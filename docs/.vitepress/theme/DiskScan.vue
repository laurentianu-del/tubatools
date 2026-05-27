<template>
  <div ref="container" class="disk-scan">
    <div class="scan-content">
      <div class="scan-visual">
        <div class="disk-frame anim-el">
          <div class="scan-header">
            <span class="scan-title"><i class="fa-solid fa-chart-pie"></i> 磁盘空间分析</span>
            <span class="scan-progress-label">{{ Math.round(scanProgress) }}%</span>
          </div>
          <div class="scan-bar-track"><div class="scan-bar-fill" :style="{ width: scanProgress + '%' }"></div></div>
          <div class="folder-grid">
            <div v-for="(folder, i) in folders" :key="i" class="folder-block" :class="{ revealed: folder.revealed }" :style="{ backgroundColor: folder.color, gridRow: folder.row, gridColumn: folder.col }">
              <i :class="folder.icon"></i>
              <span class="f-name">{{ folder.name }}</span>
              <span class="f-size">{{ folder.size }}</span>
            </div>
          </div>
        </div>
      </div>
      <div class="scan-text">
        <div class="section-badge anim-el"><i class="fa-solid fa-chart-pie"></i> 空间分析</div>
        <h3 class="anim-el">可视化磁盘空间分析</h3>
        <p class="anim-el">扫描磁盘空间占用，用彩色方块图直观展示每个文件夹的大小。快速定位大文件，释放磁盘空间，告别存储焦虑。</p>
        <div class="scan-features">
          <div class="scan-feature anim-el"><i class="fa-solid fa-bolt"></i> 超快 MFT 扫描</div>
          <div class="scan-feature anim-el"><i class="fa-solid fa-chart-pie"></i> 树状图可视化</div>
          <div class="scan-feature anim-el"><i class="fa-solid fa-magnifying-glass"></i> 大文件定位</div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted, onUnmounted, nextTick } from 'vue'
import { gsap } from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
gsap.registerPlugin(ScrollTrigger)

const container = ref(null)
const scanProgress = ref(0)
let ctx = null
let delayedCalls = []

const folders = reactive([
  { name: 'Games', size: '67.8 GB', icon: 'fa-solid fa-gamepad', color: '#34d399', row: '1/3', col: '1/3', revealed: false },
  { name: 'Users', size: '45.2 GB', icon: 'fa-solid fa-user', color: '#f472b6', row: '1/2', col: '3/5', revealed: false },
  { name: 'Windows', size: '24.3 GB', icon: 'fa-brands fa-windows', color: '#667eea', row: '3/5', col: '1/3', revealed: false },
  { name: 'Program Files', size: '18.7 GB', icon: 'fa-solid fa-folder', color: '#a855f7', row: '2/4', col: '3/5', revealed: false },
  { name: 'Updates', size: '8.5 GB', icon: 'fa-solid fa-download', color: '#fbbf24', row: '5/6', col: '1/2', revealed: false },
  { name: 'Temp', size: '3.1 GB', icon: 'fa-solid fa-fire', color: '#fb923c', row: '4/5', col: '3/4', revealed: false },
  { name: 'Drivers', size: '2.4 GB', icon: 'fa-solid fa-microchip', color: '#22d3ee', row: '4/5', col: '4/5', revealed: false },
  { name: 'Recovery', size: '1.8 GB', icon: 'fa-solid fa-rotate-left', color: '#f87171', row: '5/6', col: '2/4', revealed: false },
])

onMounted(async () => {
  if (!container.value) return
  await nextTick()
  const el = container.value
  const animEls = el.querySelectorAll('.anim-el')
  gsap.set(animEls, { opacity: 0, y: 30 })

  ctx = gsap.context(() => {
    ScrollTrigger.create({
      trigger: el,
      start: 'top 88%',
      once: true,
      onEnter: () => {
        const tl = gsap.timeline()
        tl.to(el.querySelector('.disk-frame'), { opacity: 1, y: 0, scale: 1, duration: 0.8, ease: 'power3.out' })
        tl.to(el.querySelector('.scan-text .section-badge'), { opacity: 1, y: 0, duration: 0.5, ease: 'power2.out' }, '-=0.5')
        tl.to(el.querySelector('.scan-text h3'), { opacity: 1, y: 0, duration: 0.7, ease: 'power3.out' }, '-=0.35')
        tl.to(el.querySelector('.scan-text p'), { opacity: 1, y: 0, duration: 0.6, ease: 'power3.out' }, '-=0.45')
        tl.to(el.querySelectorAll('.scan-feature'), { opacity: 1, y: 0, duration: 0.4, stagger: 0.08, ease: 'power2.out' }, '-=0.35')

        gsap.to(scanProgress, { value: 100, duration: 2.5, ease: 'none', delay: 0.3 })
        folders.forEach((f, i) => {
          const dc = gsap.delayedCall(0.5 + i * 0.25, () => { f.revealed = true })
          delayedCalls.push(dc)
        })
      }
    })
  }, el)
})

onUnmounted(() => {
  delayedCalls.forEach(dc => dc.kill())
  delayedCalls = []
  ctx?.revert()
})
</script>

<style scoped>
.disk-scan { padding: 48px 0; }
.scan-content { display: flex; gap: 48px; align-items: center; }
.scan-visual { flex: 1.3; min-width: 320px; }
.anim-el { will-change: transform, opacity; }
.disk-frame { border-radius: 16px; overflow: hidden; border: 1px solid var(--vp-c-divider); background: var(--vp-c-bg-soft); box-shadow: 0 20px 60px rgba(0,0,0,0.1); }
.scan-header { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; border-bottom: 1px solid var(--vp-c-divider); background: var(--vp-c-bg-soft); }
.scan-title { font-size: 13px; font-weight: 600; display: flex; align-items: center; gap: 8px; }
.scan-title i { color: var(--vp-c-brand-1); }
.scan-progress-label { font-size: 13px; font-weight: 700; color: var(--vp-c-brand-1); font-variant-numeric: tabular-nums; }
.scan-bar-track { height: 3px; background: var(--vp-c-divider); }
.scan-bar-fill { height: 100%; background: linear-gradient(90deg, #667eea, #a855f7); }
.folder-grid { display: grid; grid-template-columns: repeat(4, 1fr); grid-template-rows: repeat(5, 1fr); gap: 3px; padding: 3px; min-height: 300px; }
.folder-block { display: flex; flex-direction: column; align-items: center; justify-content: center; border-radius: 8px; padding: 8px; opacity: 0; transform: scale(0.5); transition: all 0.5s cubic-bezier(0.34, 1.56, 0.64, 1); color: white; text-shadow: 0 1px 3px rgba(0,0,0,0.3); }
.folder-block.revealed { opacity: 1; transform: scale(1); }
.folder-block:hover { transform: scale(1.06); z-index: 2; box-shadow: 0 6px 20px rgba(0,0,0,0.25); }
.folder-block i { font-size: 20px; margin-bottom: 4px; }
.f-name { font-size: 11px; font-weight: 600; white-space: nowrap; }
.f-size { font-size: 10px; opacity: 0.85; }
.scan-text { flex: 1; min-width: 280px; }
.section-badge { display: inline-flex; align-items: center; gap: 6px; padding: 5px 14px; border-radius: 20px; font-size: 13px; font-weight: 600; background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1); margin-bottom: 16px; border: 1px solid rgba(91,95,199,0.15); }
.scan-text h3 { font-size: 28px; font-weight: 700; margin: 0 0 16px; line-height: 1.3; }
.scan-text p { font-size: 16px; color: var(--vp-c-text-2); line-height: 1.7; margin: 0 0 24px; }
.scan-features { display: flex; flex-direction: column; gap: 10px; }
.scan-feature { display: flex; align-items: center; gap: 10px; padding: 10px 16px; background: var(--vp-c-bg-soft); border: 1px solid var(--vp-c-divider); border-radius: 10px; font-size: 14px; font-weight: 500; transition: border-color 0.2s, transform 0.2s; }
.scan-feature:hover { border-color: var(--vp-c-brand-1); transform: translateX(4px); }
.scan-feature i { color: var(--vp-c-brand-1); width: 18px; text-align: center; }
@media (max-width: 768px) { .scan-content { flex-direction: column; } .scan-text h3 { font-size: 22px; } }
</style>