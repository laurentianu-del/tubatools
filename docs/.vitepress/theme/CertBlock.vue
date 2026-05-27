<template>
  <div ref="container" class="cert-block">
    <div class="cert-content">
      <div class="cert-text">
        <div class="section-badge anim-el"><i class="fa-solid fa-shield-halved"></i> 安全防护</div>
        <h3 class="anim-el">证书屏蔽，拦截流氓软件</h3>
        <p class="anim-el">通过管理 Windows 证书存储中的"不信任"列表，从根源上阻止流氓软件利用数字证书获取信任。一键屏蔽，守护系统安全。</p>
        <div class="cert-steps">
          <div class="cert-step anim-el"><span class="step-num">1</span> 查看已安装的数字证书</div>
          <div class="cert-step anim-el"><span class="step-num">2</span> 选择需要屏蔽的流氓证书</div>
          <div class="cert-step anim-el"><span class="step-num">3</span> 一键加入不信任列表</div>
        </div>
      </div>
      <div class="cert-visual">
        <div class="software-grid">
          <div v-for="(sw, i) in software" :key="i" class="software-item anim-el" :class="{ blocked: sw.blocked }">
            <div class="sw-icon"><i :class="sw.icon"></i></div>
            <span class="sw-name">{{ sw.name }}</span>
            <div class="block-mark"><i class="fa-solid fa-xmark"></i></div>
          </div>
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
let ctx = null
let delayedCalls = []

const software = reactive([
  { name: '2345浏览器', icon: 'fa-solid fa-globe', blocked: false },
  { name: '毒霸', icon: 'fa-solid fa-shield-virus', blocked: false },
  { name: '360安全', icon: 'fa-solid fa-shield', blocked: false },
  { name: '瑞星', icon: 'fa-solid fa-bug', blocked: false },
  { name: '万能壁纸', icon: 'fa-solid fa-image', blocked: false },
  { name: '快压', icon: 'fa-solid fa-file-zipper', blocked: false },
  { name: '驱动精灵', icon: 'fa-solid fa-wrench', blocked: false },
  { name: '小鸟壁纸', icon: 'fa-solid fa-palette', blocked: false },
])

onMounted(async () => {
  if (!container.value) return
  await nextTick()
  const el = container.value
  const animEls = el.querySelectorAll('.anim-el')
  gsap.set(animEls, { opacity: 0, y: 25 })

  ctx = gsap.context(() => {
    ScrollTrigger.create({
      trigger: el,
      start: 'top 88%',
      once: true,
      onEnter: () => {
        const tl = gsap.timeline()
        tl.to(el.querySelector('.cert-text .section-badge'), { opacity: 1, y: 0, duration: 0.5, ease: 'power2.out' })
        tl.to(el.querySelector('.cert-text h3'), { opacity: 1, y: 0, duration: 0.7, ease: 'power3.out' }, '-=0.35')
        tl.to(el.querySelector('.cert-text p'), { opacity: 1, y: 0, duration: 0.6, ease: 'power3.out' }, '-=0.45')
        tl.to(el.querySelectorAll('.cert-step'), { opacity: 1, y: 0, duration: 0.4, stagger: 0.1, ease: 'power2.out' }, '-=0.35')
        tl.to(el.querySelectorAll('.software-item'), { opacity: 1, y: 0, scale: 1, rotation: 0, duration: 0.45, stagger: 0.08, ease: 'back.out(1.7)' }, 0.15)

        software.forEach((sw, i) => {
          const dc = gsap.delayedCall(1.5 + i * 0.3, () => { sw.blocked = true })
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
.cert-block { padding: 48px 0; }
.cert-content { display: flex; gap: 48px; align-items: center; }
.cert-text { flex: 1; min-width: 280px; }
.anim-el { will-change: transform, opacity; }
.section-badge { display: inline-flex; align-items: center; gap: 6px; padding: 5px 14px; border-radius: 20px; font-size: 13px; font-weight: 600; background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1); margin-bottom: 16px; border: 1px solid rgba(91,95,199,0.15); }
.cert-text h3 { font-size: 28px; font-weight: 700; margin: 0 0 16px; line-height: 1.3; }
.cert-text p { font-size: 16px; color: var(--vp-c-text-2); line-height: 1.7; margin: 0 0 24px; }
.cert-steps { display: flex; flex-direction: column; gap: 10px; }
.cert-step { display: flex; align-items: center; gap: 12px; padding: 10px 16px; background: var(--vp-c-bg-soft); border: 1px solid var(--vp-c-divider); border-radius: 10px; font-size: 14px; font-weight: 500; transition: border-color 0.2s, transform 0.2s; }
.cert-step:hover { border-color: var(--vp-c-brand-1); transform: translateX(4px); }
.step-num { width: 24px; height: 24px; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 12px; font-weight: 700; background: linear-gradient(135deg, #667eea, #764ba2); color: white; flex-shrink: 0; }
.cert-visual { flex: 1.2; min-width: 320px; }
.software-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; }
.software-item {
  position: relative; display: flex; flex-direction: column; align-items: center;
  gap: 8px; padding: 18px 8px; border-radius: 14px;
  background: var(--vp-c-bg-soft); border: 2px solid var(--vp-c-divider);
  transition: border-color 0.5s, background 0.5s, box-shadow 0.5s;
}
.software-item.blocked { border-color: #ef4444; background: rgba(239,68,68,0.06); box-shadow: 0 0 24px rgba(239,68,68,0.1); }
.software-item.blocked .sw-icon { opacity: 0.3; background: rgba(239,68,68,0.1); color: #ef4444; }
.software-item.blocked .sw-name { opacity: 0.35; text-decoration: line-through; color: var(--vp-c-text-2); }
.sw-icon { width: 44px; height: 44px; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 20px; background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1); transition: all 0.5s; }
.sw-name { font-size: 12px; font-weight: 600; white-space: nowrap; transition: all 0.4s; color: var(--vp-c-text-1); }
.block-mark {
  position: absolute; top: -8px; right: -8px; width: 28px; height: 28px;
  border-radius: 50%; background: linear-gradient(135deg, #ef4444, #dc2626); color: white;
  display: flex; align-items: center; justify-content: center; font-size: 15px; font-weight: 900;
  opacity: 0; transform: scale(0) rotate(-45deg);
  transition: all 0.45s cubic-bezier(0.34, 1.56, 0.64, 1);
  box-shadow: 0 4px 14px rgba(239,68,68,0.5);
}
.software-item.blocked .block-mark { opacity: 1; transform: scale(1) rotate(0deg); }
@media (max-width: 768px) { .cert-content { flex-direction: column; } .software-grid { grid-template-columns: repeat(2, 1fr); } .cert-text h3 { font-size: 22px; } }
</style>