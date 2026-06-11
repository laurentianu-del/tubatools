<template>
  <div class="cert-block">
    <div class="cert-content">
      <div class="cert-text">
        <div class="section-badge"><i class="fa-solid fa-shield-halved"></i> 安全防护</div>
        <h3>证书屏蔽，拦截流氓软件</h3>
        <p>通过管理 Windows 证书存储中的"不信任"列表，从根源上阻止流氓软件利用数字证书获取信任。一键屏蔽，守护系统安全。</p>
        <div class="cert-steps">
          <div class="cert-step"><span class="step-num">1</span> 查看已安装的数字证书</div>
          <div class="cert-step"><span class="step-num">2</span> 选择需要屏蔽的流氓证书</div>
          <div class="cert-step"><span class="step-num">3</span> 一键加入不信任列表</div>
        </div>
      </div>
      <div class="cert-visual">
        <div class="software-grid">
          <div v-for="(sw, i) in software" :key="i" class="software-item blocked">
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
const software = [
  { name: '2345浏览器', icon: 'fa-solid fa-globe' },
  { name: '毒霸', icon: 'fa-solid fa-shield-virus' },
  { name: '360安全', icon: 'fa-solid fa-shield' },
  { name: '瑞星', icon: 'fa-solid fa-bug' },
  { name: '万能壁纸', icon: 'fa-solid fa-image' },
  { name: '快压', icon: 'fa-solid fa-file-zipper' },
  { name: '驱动精灵', icon: 'fa-solid fa-wrench' },
  { name: '小鸟壁纸', icon: 'fa-solid fa-palette' },
]
</script>

<style scoped>
.cert-block { padding: 48px 0; }
.cert-content { display: flex; gap: 48px; align-items: center; }
.cert-text { flex: 1; min-width: 280px; }
.section-badge {
  display: inline-flex; align-items: center; gap: 6px; padding: 4px 12px;
  border-radius: 6px; font-size: 12px; font-weight: 600;
  background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1);
  border: 1px solid var(--fluent-stroke); margin-bottom: 16px;
}
.cert-text h3 { font-size: 28px; font-weight: 700; margin: 0 0 16px; line-height: 1.3; }
.cert-text p { font-size: 15px; color: var(--vp-c-text-2); line-height: 1.7; margin: 0 0 24px; }
.cert-steps { display: flex; flex-direction: column; gap: 10px; }
.cert-step {
  display: flex; align-items: center; gap: 12px; padding: 10px 16px;
  background: var(--vp-c-bg-soft); border: 1px solid var(--fluent-stroke);
  border-bottom: 2px solid var(--fluent-stroke-strong);
  border-radius: 8px; font-size: 14px; font-weight: 500;
  transition: border-color 0.2s cubic-bezier(0.33, 0, 0.2, 1),
             box-shadow 0.2s cubic-bezier(0.33, 0, 0.2, 1),
             transform 0.2s cubic-bezier(0.33, 0, 0.2, 1);
}
.cert-step:hover { border-color: var(--vp-c-brand-1); box-shadow: var(--fluent-shadow); transform: translateX(4px); }
.step-num {
  width: 24px; height: 24px; border-radius: 50%;
  display: flex; align-items: center; justify-content: center;
  font-size: 12px; font-weight: 700;
  background: var(--vp-c-brand-1); color: white; flex-shrink: 0;
}
.cert-visual { flex: 1.2; min-width: 320px; }
.software-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; }
.software-item {
  position: relative; display: flex; flex-direction: column;
  align-items: center; gap: 8px; padding: 18px 8px;
  border-radius: 8px; background: var(--vp-c-bg-soft);
  border: 1px solid var(--fluent-stroke);
  border-bottom: 2px solid var(--fluent-stroke-strong);
  transition: transform 0.2s cubic-bezier(0.33, 0, 0.2, 1);
}
.software-item:hover { transform: translateY(-2px); }
.software-item.blocked {
  border-color: #c42b1c; background: rgba(196, 43, 28, 0.03);
}
.software-item.blocked .sw-icon { opacity: 0.3; background: rgba(196, 43, 28, 0.06); color: #c42b1c; }
.software-item.blocked .sw-name { opacity: 0.35; text-decoration: line-through; color: var(--vp-c-text-2); }
.sw-icon {
  width: 44px; height: 44px; border-radius: 8px;
  display: flex; align-items: center; justify-content: center;
  font-size: 20px; background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1);
}
.sw-name { font-size: 12px; font-weight: 600; white-space: nowrap; color: var(--vp-c-text-1); }
.block-mark {
  position: absolute; top: -6px; right: -6px;
  width: 22px; height: 22px; border-radius: 50%;
  background: #c42b1c; color: white;
  display: flex; align-items: center; justify-content: center;
  font-size: 11px; font-weight: 900;
  box-shadow: 0 2px 6px rgba(196, 43, 28, 0.25);
}
@media (max-width: 768px) { .cert-content { flex-direction: column; } .software-grid { grid-template-columns: repeat(2, 1fr); } .cert-text h3 { font-size: 22px; } }
</style>
