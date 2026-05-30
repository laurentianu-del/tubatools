<template>
  <div ref="container" class="hardware-info">
    <div class="hw-content">
      <div class="hw-visual">
        <div class="hw-frame anim-el">
          <div class="frame-bar">
            <span class="frame-dot r"></span>
            <span class="frame-dot y"></span>
            <span class="frame-dot g"></span>
          </div>
          <img src="/screenshot-hardware.png" alt="硬件信息界面" class="hw-img" />
        </div>
      </div>
      <div class="hw-text">
        <div class="section-badge anim-el"><i class="fa-solid fa-microchip"></i> 硬件信息</div>
        <h3 class="anim-el">全面硬件信息，一目了然</h3>
        <p class="anim-el">基于 WMI 实时查询，读取处理器、主板、内存、显卡、硬盘、网卡、声卡、显示器等全面硬件数据。后台线程执行，不阻塞 UI。</p>
        <div class="hw-items">
          <div class="hw-item anim-el" v-for="(item, i) in hwList" :key="i">
            <i :class="item.icon" :style="{ color: item.color }"></i>
            <span>{{ item.text }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, nextTick } from 'vue'
import { gsap } from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
gsap.registerPlugin(ScrollTrigger)

const container = ref(null)
let ctx = null

const hwList = [
  { icon: 'fa-solid fa-microchip', color: '#667eea', text: '处理器型号、核心/线程数、频率' },
  { icon: 'fa-solid fa-display', color: '#34d399', text: '显卡型号、显存、驱动版本' },
  { icon: 'fa-solid fa-memory', color: '#a855f7', text: '内存容量、频率、插槽信息' },
  { icon: 'fa-solid fa-hard-drive', color: '#fb923c', text: '硬盘型号、容量、健康状态' },
  { icon: 'fa-solid fa-desktop', color: '#22d3ee', text: '显示器分辨率、刷新率' },
]

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
        tl.to(el.querySelector('.hw-frame'), { opacity: 1, y: 0, x: 0, scale: 1, duration: 0.9, ease: 'power3.out' })
        tl.to(el.querySelector('.hw-text .section-badge'), { opacity: 1, y: 0, duration: 0.5, ease: 'power2.out' }, '-=0.5')
        tl.to(el.querySelector('.hw-text h3'), { opacity: 1, y: 0, duration: 0.7, ease: 'power3.out' }, '-=0.35')
        tl.to(el.querySelector('.hw-text p'), { opacity: 1, y: 0, duration: 0.6, ease: 'power3.out' }, '-=0.45')
        tl.to(el.querySelectorAll('.hw-item'), { opacity: 1, y: 0, duration: 0.4, stagger: 0.06, ease: 'power2.out' }, '-=0.35')
      }
    })
  }, el)
})

onUnmounted(() => {
  ctx?.revert()
})
</script>

<style scoped>
.hardware-info { padding: 48px 0; }
.hw-content { display: flex; gap: 48px; align-items: center; }
.hw-visual { flex: 1.2; min-width: 320px; }
.anim-el { will-change: transform, opacity; }
.hw-frame { border-radius: 16px; overflow: hidden; border: 1px solid var(--vp-c-divider); box-shadow: 0 20px 60px rgba(0,0,0,0.12); position: relative; transition: box-shadow 0.4s, transform 0.4s; }
.hw-frame:hover { box-shadow: 0 28px 80px rgba(91,95,199,0.18); transform: translateY(-4px); }
.frame-bar { display: flex; gap: 6px; padding: 10px 14px; background: var(--vp-c-bg-soft); border-bottom: 1px solid var(--vp-c-divider); }
.frame-dot { width: 10px; height: 10px; border-radius: 50%; }
.frame-dot.r { background: #ff5f57; } .frame-dot.y { background: #febc2e; } .frame-dot.g { background: #28c840; }
.hw-img { width: 100%; display: block; }
.hw-text { flex: 1; min-width: 280px; }
.section-badge { display: inline-flex; align-items: center; gap: 6px; padding: 5px 14px; border-radius: 20px; font-size: 13px; font-weight: 600; background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1); margin-bottom: 16px; border: 1px solid rgba(91,95,199,0.15); }
.hw-text h3 { font-size: 28px; font-weight: 700; margin: 0 0 16px; line-height: 1.3; }
.hw-text p { font-size: 16px; color: var(--vp-c-text-2); line-height: 1.7; margin: 0 0 24px; }
.hw-items { display: flex; flex-direction: column; gap: 10px; }
.hw-item { display: flex; align-items: center; gap: 10px; padding: 10px 16px; background: var(--vp-c-bg-soft); border: 1px solid var(--vp-c-divider); border-radius: 10px; font-size: 14px; transition: border-color 0.2s, transform 0.2s; }
.hw-item:hover { border-color: var(--vp-c-brand-1); transform: translateX(4px); }
.hw-item i { width: 18px; text-align: center; }
@media (max-width: 768px) { .hw-content { flex-direction: column; } .hw-text h3 { font-size: 22px; } }
</style>