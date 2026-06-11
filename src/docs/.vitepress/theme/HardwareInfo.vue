<template>
  <div class="hardware-info">
    <div class="hw-content">
      <div class="hw-visual">
        <div class="image-shell">
          <div class="hw-frame">
            <div class="frame-bar">
              <span class="frame-dot r"></span>
              <span class="frame-dot y"></span>
              <span class="frame-dot g"></span>
            </div>
            <img src="/screenshot-hardware.png" alt="硬件信息界面" class="hw-img" />
          </div>
        </div>
      </div>
      <div class="hw-text">
        <div class="section-badge"><i class="fa-solid fa-microchip"></i> 硬件信息</div>
        <h3>全面硬件信息，一目了然</h3>
        <p>基于 WMI 实时查询，读取处理器、主板、内存、显卡、硬盘、网卡、声卡、显示器等全面硬件数据。后台线程执行，不阻塞 UI。</p>
        <div class="hw-items">
          <div class="hw-item" v-for="(item, i) in hwList" :key="i">
            <div class="hw-item-icon" :style="{ background: item.bg, color: item.color }"><i :class="item.icon"></i></div>
            <span>{{ item.text }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
const hwList = [
  { icon: 'fa-solid fa-microchip', color: '#0078d4', bg: 'rgba(0,120,212,0.08)', text: '处理器型号、核心/线程数、频率' },
  { icon: 'fa-solid fa-display', color: '#107c10', bg: 'rgba(16,124,16,0.08)', text: '显卡型号、显存、驱动版本' },
  { icon: 'fa-solid fa-memory', color: '#8764b8', bg: 'rgba(135,100,184,0.08)', text: '内存容量、频率、插槽信息' },
  { icon: 'fa-solid fa-hard-drive', color: '#ca5010', bg: 'rgba(202,80,16,0.08)', text: '硬盘型号、容量、健康状态' },
  { icon: 'fa-solid fa-desktop', color: '#008272', bg: 'rgba(0,130,114,0.08)', text: '显示器分辨率、刷新率' },
]
</script>

<style scoped>
.hardware-info { padding: 48px 0; }
.hw-content { display: flex; gap: 48px; align-items: center; }
.hw-visual { flex: 1.2; min-width: 320px; }
.image-shell {
  padding: 4px; border-radius: 14px;
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--fluent-stroke);
  box-shadow: var(--fluent-shadow-card);
  transition: box-shadow 0.25s cubic-bezier(0.33, 0, 0.2, 1);
}
.image-shell:hover { box-shadow: var(--fluent-shadow-card-hover); }
.hw-frame { border-radius: 10px; overflow: hidden; background: var(--vp-c-bg-soft); }
.frame-bar { display: flex; gap: 6px; padding: 10px 14px; background: var(--vp-c-bg-soft); border-bottom: 1px solid var(--fluent-stroke); }
.frame-dot { width: 10px; height: 10px; border-radius: 50%; }
.frame-dot.r { background: #ff5f57; } .frame-dot.y { background: #febc2e; } .frame-dot.g { background: #28c840; }
.hw-img { width: 100%; display: block; }
.hw-text { flex: 1; min-width: 280px; }
.section-badge {
  display: inline-flex; align-items: center; gap: 6px; padding: 4px 12px;
  border-radius: 6px; font-size: 12px; font-weight: 600;
  background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1);
  border: 1px solid var(--fluent-stroke); margin-bottom: 16px;
}
.hw-text h3 { font-size: 28px; font-weight: 700; margin: 0 0 16px; line-height: 1.3; }
.hw-text p { font-size: 15px; color: var(--vp-c-text-2); line-height: 1.7; margin: 0 0 24px; }
.hw-items { display: flex; flex-direction: column; gap: 8px; }
.hw-item {
  display: flex; align-items: center; gap: 12px; padding: 10px 16px;
  background: var(--vp-c-bg-soft); border: 1px solid var(--fluent-stroke);
  border-left: 3px solid var(--vp-c-brand-1);
  border-radius: 8px; font-size: 14px;
  transition: border-color 0.2s cubic-bezier(0.33, 0, 0.2, 1),
             box-shadow 0.2s cubic-bezier(0.33, 0, 0.2, 1),
             transform 0.2s cubic-bezier(0.33, 0, 0.2, 1);
}
.hw-item:hover { border-color: var(--vp-c-brand-1); box-shadow: var(--fluent-shadow); transform: translateX(4px); }
.hw-item-icon {
  width: 28px; height: 28px; border-radius: 6px;
  display: flex; align-items: center; justify-content: center;
  font-size: 13px; flex-shrink: 0;
}
@media (max-width: 768px) { .hw-content { flex-direction: column; } .hw-text h3 { font-size: 22px; } }
</style>
