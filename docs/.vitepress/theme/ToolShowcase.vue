<template>
  <div ref="container" class="tool-showcase">
    <div class="showcase-content">
      <div class="showcase-text">
        <div class="section-badge anim-el"><i class="fa-solid fa-rocket"></i> 工具目录</div>
        <h3 class="anim-el">94 款专业工具，一键启动</h3>
        <p class="anim-el">自动扫描本地工具目录，按处理器、显卡、硬盘、内存等 8 大分类整齐归档。支持实时搜索、收藏夹、管理员运行、创建桌面快捷方式。</p>
        <div class="tool-tags">
          <span v-for="tag in tags" :key="tag" class="tool-tag anim-el">{{ tag }}</span>
        </div>
      </div>
      <div class="showcase-image-wrap">
        <div class="image-frame anim-el">
          <div class="frame-bar">
            <span class="frame-dot r"></span>
            <span class="frame-dot y"></span>
            <span class="frame-dot g"></span>
          </div>
          <img src="/screenshot-tools.png" alt="图吧工具箱界面" class="showcase-img" />
          <div class="image-glow"></div>
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
const tags = ['CPU-Z', 'GPU-Z', 'FurMark', 'CrystalDiskMark', 'AIDA64', 'HWiNFO', 'DiskGenius', 'Everything']

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
        tl.to(el.querySelector('.section-badge'), { opacity: 1, y: 0, duration: 0.5, ease: 'power2.out' })
        tl.to(el.querySelector('.showcase-text h3'), { opacity: 1, y: 0, duration: 0.7, ease: 'power3.out' }, '-=0.35')
        tl.to(el.querySelector('.showcase-text p'), { opacity: 1, y: 0, duration: 0.6, ease: 'power3.out' }, '-=0.45')
        tl.to(el.querySelectorAll('.tool-tag'), { opacity: 1, y: 0, scale: 1, rotation: 0, duration: 0.4, stagger: 0.05, ease: 'back.out(1.7)' }, '-=0.35')
        tl.to(el.querySelector('.image-frame'), { opacity: 1, y: 0, x: 0, scale: 1, duration: 0.9, ease: 'power3.out' }, 0.1)
        tl.to(el.querySelector('.showcase-img'), { opacity: 1, scale: 1, duration: 0.8, ease: 'power2.out' }, 0.3)
        tl.to(el.querySelector('.image-glow'), { opacity: 1, scale: 1, duration: 0.6, ease: 'power2.out' }, 0.5)
      }
    })
  }, el)
})

onUnmounted(() => { ctx?.revert() })
</script>

<style scoped>
.tool-showcase { padding: 48px 0; }
.showcase-content { display: flex; gap: 48px; align-items: center; }
.showcase-text { flex: 1; min-width: 280px; }
.anim-el { will-change: transform, opacity; }
.section-badge { display: inline-flex; align-items: center; gap: 6px; padding: 5px 14px; border-radius: 20px; font-size: 13px; font-weight: 600; background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1); margin-bottom: 16px; border: 1px solid rgba(91,95,199,0.15); }
.showcase-text h3 { font-size: 28px; font-weight: 700; margin: 0 0 16px; line-height: 1.3; }
.showcase-text p { font-size: 16px; color: var(--vp-c-text-2); line-height: 1.7; margin: 0 0 24px; }
.tool-tags { display: flex; flex-wrap: wrap; gap: 8px; }
.tool-tag { padding: 6px 14px; border-radius: 20px; font-size: 13px; font-weight: 500; background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1); border: 1px solid rgba(91,95,199,0.15); transition: transform 0.25s, box-shadow 0.25s; }
.tool-tag:hover { transform: translateY(-3px) scale(1.05); box-shadow: 0 6px 16px rgba(91,95,199,0.2); }
.showcase-image-wrap { flex: 1.2; min-width: 320px; }
.image-frame { position: relative; border-radius: 16px; overflow: hidden; border: 1px solid var(--vp-c-divider); box-shadow: 0 20px 60px rgba(0,0,0,0.12); transition: box-shadow 0.4s, transform 0.4s; }
.image-frame:hover { box-shadow: 0 28px 80px rgba(91,95,199,0.18); transform: translateY(-4px); }
.frame-bar { display: flex; gap: 6px; padding: 10px 14px; background: var(--vp-c-bg-soft); border-bottom: 1px solid var(--vp-c-divider); }
.frame-dot { width: 10px; height: 10px; border-radius: 50%; }
.frame-dot.r { background: #ff5f57; } .frame-dot.y { background: #febc2e; } .frame-dot.g { background: #28c840; }
.showcase-img { width: 100%; display: block; }
.image-glow { position: absolute; bottom: -30px; left: 50%; transform: translateX(-50%); width: 80%; height: 60px; border-radius: 50%; background: radial-gradient(ellipse, rgba(91,95,199,0.25) 0%, transparent 70%); filter: blur(16px); pointer-events: none; opacity: 0; }
@media (max-width: 768px) { .showcase-content { flex-direction: column; } .showcase-text h3 { font-size: 22px; } }
</style>