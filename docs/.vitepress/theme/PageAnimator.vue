<template>
  <div ref="container"></div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, nextTick } from 'vue'
import { gsap } from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
gsap.registerPlugin(ScrollTrigger)

const container = ref(null)
let ctx = null

function animateGroup(triggerEl, selectors, fromVars, toVars, stagger = 0) {
  const items = triggerEl.querySelectorAll(selectors)
  if (!items.length) return
  gsap.set(items, fromVars)
  ScrollTrigger.create({
    trigger: triggerEl,
    start: 'top 88%',
    once: true,
    onEnter: () => {
      gsap.to(items, { ...toVars, stagger, overwrite: true })
    }
  })
}

onMounted(async () => {
  if (!container.value) return
  await nextTick()

  const root = container.value.closest('.vp-doc') || document.body

  ctx = gsap.context(() => {
    const sg = root.querySelector('.stat-grid')
    const fg = root.querySelector('.features-grid')
    const cg = root.querySelector('.category-grid')
    const tr = root.querySelector('.tech-row')

    if (sg) {
      const items = sg.querySelectorAll('.stat-item')
      gsap.set(items, { opacity: 0, y: 30, scale: 0.85 })
      ScrollTrigger.create({
        trigger: sg, start: 'top 88%', once: true,
        onEnter: () => { gsap.to(items, { opacity: 1, y: 0, scale: 1, duration: 0.6, stagger: 0.1, ease: 'back.out(1.7)', overwrite: true }) }
      })
    }

    if (fg) {
      const cards = fg.querySelectorAll('.feature-card')
      const icons = fg.querySelectorAll('.feature-icon')
      gsap.set(cards, { opacity: 0, y: 40, scale: 0.92 })
      gsap.set(icons, { opacity: 0, scale: 0, rotation: -15 })
      ScrollTrigger.create({
        trigger: fg, start: 'top 88%', once: true,
        onEnter: () => {
          gsap.to(cards, { opacity: 1, y: 0, scale: 1, duration: 0.7, stagger: 0.08, ease: 'power3.out', overwrite: true })
          gsap.to(icons, { opacity: 1, scale: 1, rotation: 0, duration: 0.5, stagger: 0.08, ease: 'back.out(2)', delay: 0.15, overwrite: true })
        }
      })
    }

    root.querySelectorAll('h2').forEach(h2 => {
      gsap.set(h2, { opacity: 0, y: 25 })
      ScrollTrigger.create({
        trigger: h2, start: 'top 88%', once: true,
        onEnter: () => { gsap.to(h2, { opacity: 1, y: 0, duration: 0.8, ease: 'power3.out', overwrite: true }) }
      })
    })

    if (cg) {
      const cards = cg.querySelectorAll('.category-card')
      const icons = cg.querySelectorAll('.category-icon')
      gsap.set(cards, { opacity: 0, y: 30, scale: 0.85 })
      gsap.set(icons, { opacity: 0, scale: 0, rotation: 20 })
      ScrollTrigger.create({
        trigger: cg, start: 'top 88%', once: true,
        onEnter: () => {
          gsap.to(cards, { opacity: 1, y: 0, scale: 1, duration: 0.5, stagger: 0.06, ease: 'back.out(1.5)', overwrite: true })
          gsap.to(icons, { opacity: 1, scale: 1, rotation: 0, duration: 0.45, stagger: 0.06, ease: 'back.out(2)', delay: 0.1, overwrite: true })
        }
      })
    }

    if (tr) {
      const cards = tr.querySelectorAll('.tech-card')
      const icons = tr.querySelectorAll('.tech-icon')
      gsap.set(cards, { opacity: 0, y: 25, scale: 0.9 })
      gsap.set(icons, { opacity: 0, scale: 0 })
      ScrollTrigger.create({
        trigger: tr, start: 'top 88%', once: true,
        onEnter: () => {
          gsap.to(cards, { opacity: 1, y: 0, scale: 1, duration: 0.5, stagger: 0.08, ease: 'power2.out', overwrite: true })
          gsap.to(icons, { opacity: 1, scale: 1, duration: 0.4, stagger: 0.08, ease: 'back.out(2)', delay: 0.1, overwrite: true })
        }
      })
    }

    root.querySelectorAll('ul').forEach(ul => {
      const lis = ul.querySelectorAll('li')
      if (!lis.length) return
      gsap.set(lis, { opacity: 0, x: -15 })
      ScrollTrigger.create({
        trigger: ul, start: 'top 88%', once: true,
        onEnter: () => { gsap.to(lis, { opacity: 1, x: 0, duration: 0.4, stagger: 0.06, ease: 'power2.out', overwrite: true }) }
      })
    })
  }, root)
})

onUnmounted(() => { ctx?.revert() })
</script>